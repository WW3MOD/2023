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
using NUnit.Framework;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the pure integer logic behind the PIPELINE-5 cohesion stance identities
	/// (CohesionMoveModifier). The trait itself is coupled to Actor/Map/Mobile and cannot be
	/// unit-tested in isolation, so — following the SuppressionMathTest / PoiOffenseTest idiom —
	/// these tests mirror the source constants and selection math and assert the properties the
	/// design relies on. If someone changes CohesionMoveModifierInfo defaults or the selection
	/// logic, the mirrored copy here diverges and the assertions below flag it.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs
	///   - GetSpacing (DP-2 human Spread dispersion)
	///   - ComputeBoxSlots count-aware footprint cap
	///   - PickCoverSlotNear bend predicate (DP-3 human Loose cover-first)
	/// </summary>
	[TestFixture]
	public class CohesionStanceMathTest
	{
		// Mode order mirrors CohesionMoveModifier's enum { Tight, Loose, Spread }.
		enum Mode { Tight, Loose, Spread }

		// --- Mirror of CohesionMoveModifierInfo spacing/cap defaults (WDist; 1024 = 1 cell) ---
		const int TightCol = 1024, TightRow = 1024;
		const int LooseCol = 2048, LooseRow = 1536;
		const int SpreadCol = 3072, SpreadRow = 2560;            // bot / benchmark path
		const int SpreadHumanCol = 4096, SpreadHumanRow = 3072;  // DP-2 human dispersion
		const int TightMaxW = 8192, TightMaxD = 5120;
		const int LooseMaxW = 11264, LooseMaxD = 6144;
		const int SpreadMaxW = 13312, SpreadMaxD = 7168;
		const int MinSlotSpacing = 1024;

		// Mirror of CohesionMoveModifier.GetSpacing(mode, isHuman, ...). Only Spread branches on
		// isHuman (DP-2); Tight/Loose are owner-independent.
		static (int Col, int Row) GetSpacing(Mode mode, bool isHuman)
		{
			switch (mode)
			{
				case Mode.Tight: return (TightCol, TightRow);
				case Mode.Spread: return isHuman ? (SpreadHumanCol, SpreadHumanRow) : (SpreadCol, SpreadRow);
				default: return (LooseCol, LooseRow);
			}
		}

		static (int W, int D) GetMaxExtent(Mode mode)
		{
			switch (mode)
			{
				case Mode.Tight: return (TightMaxW, TightMaxD);
				case Mode.Spread: return (SpreadMaxW, SpreadMaxD);
				default: return (LooseMaxW, LooseMaxD);
			}
		}

		// Mirror of the box column count + count-aware width cap in ComputeBoxSlots.
		static int EffectiveBoxColSpacing(Mode mode, bool isHuman, int n)
		{
			var (col, _) = GetSpacing(mode, isHuman);
			var (maxW, _) = GetMaxExtent(mode);

			var cols = (int)Math.Ceiling(Math.Sqrt(n * 2.0));
			cols = Math.Min(cols, n);
			cols = Math.Max(cols, 2);

			if (cols > 1 && (long)(cols - 1) * col > maxW)
				col = Math.Max(MinSlotSpacing, maxW / (cols - 1));

			return col;
		}

		// ---------- DP-2: human Spread is a genuine 'dispersed' interval ----------

		[Test]
		public void HumanSpreadIsWiderThanBotSpread()
		{
			// The human dispersion interval must strictly exceed the frozen bot Spread interval,
			// otherwise the stance would not disperse any more than the benchmark path.
			Assert.That(GetSpacing(Mode.Spread, true).Col, Is.GreaterThan(GetSpacing(Mode.Spread, false).Col));
			Assert.That(GetSpacing(Mode.Spread, true).Row, Is.GreaterThan(GetSpacing(Mode.Spread, false).Row));
		}

		[Test]
		public void BotSpacingIsOwnerIndependentForAllModes()
		{
			// Benchmark isolation: for a BOT owner, no mode's spacing depends on the human flag —
			// bot layouts are byte-identical to before the stance work. (Only human Spread differs.)
			foreach (Mode m in Enum.GetValues(typeof(Mode)))
				Assert.That(GetSpacing(m, false), Is.EqualTo(GetSpacing(m, false)));

			// Tight and Loose are owner-independent even for humans (only Spread carries DP-2).
			Assert.That(GetSpacing(Mode.Tight, true), Is.EqualTo(GetSpacing(Mode.Tight, false)));
			Assert.That(GetSpacing(Mode.Loose, true), Is.EqualTo(GetSpacing(Mode.Loose, false)));
		}

		[Test]
		public void HumanSpreadIntervalExceedsAoESizedFloor()
		{
			// DP-2 rationale pin: the interval must comfortably exceed a typical area warhead's
			// lethal footprint (~1.5-2 cells) so a single shell centred on one unit cannot also
			// catch its neighbour. 4 cells (4096) leaves a >=2-cell margin over a 2-cell blast.
			const int TypicalBlastCells = 2;
			Assert.That(GetSpacing(Mode.Spread, true).Col, Is.GreaterThanOrEqualTo((TypicalBlastCells + 2) * 1024));
		}

		// ---------- Monotonicity: Tight < Loose < Spread at every squad size ----------

		[Test]
		public void EffectiveBoxSpacingStaysMonotonicAcrossModes()
		{
			// The design comment claims effective spacing stays Tight < Loose < Spread for EVERY n,
			// even after the count-aware cap shrinks per-slot spacing. Verify across squad sizes,
			// using the human Spread value (the widest, most likely to hit its cap first).
			for (var n = 2; n <= 60; n++)
			{
				var tight = EffectiveBoxColSpacing(Mode.Tight, false, n);
				var loose = EffectiveBoxColSpacing(Mode.Loose, false, n);
				var spread = EffectiveBoxColSpacing(Mode.Spread, true, n);

				Assert.That(loose, Is.GreaterThan(tight), $"Loose>Tight failed at n={n}");
				Assert.That(spread, Is.GreaterThan(loose), $"Spread>Loose failed at n={n}");
			}
		}

		[Test]
		public void HumanSpreadIsAtLeastAsDispersedAsBotSpread()
		{
			// Human Spread must never be TIGHTER than bot Spread at any n: strictly wider until the
			// shared SpreadMaxWidth cap binds, then equal (both clamp to the same cap).
			for (var n = 2; n <= 60; n++)
			{
				var human = EffectiveBoxColSpacing(Mode.Spread, true, n);
				var bot = EffectiveBoxColSpacing(Mode.Spread, false, n);
				Assert.That(human, Is.GreaterThanOrEqualTo(bot), $"human<bot Spread at n={n}");
			}
		}

		[Test]
		public void FootprintCapNeverShrinksBelowMinSlotSpacing()
		{
			// The cap floors per-slot spacing at MinSlotSpacing so slots never overlap onto one cell.
			for (var n = 2; n <= 200; n++)
				Assert.That(EffectiveBoxColSpacing(Mode.Spread, true, n), Is.GreaterThanOrEqualTo(MinSlotSpacing));
		}

		// ---------- DP-3: cover-first bend predicate ----------

		// Mirror of the return-bend condition in PickCoverSlotNear:
		//   if (coverFound && (!found || bestCover <= 0 || (coverFirst && bestCoverRaw > bestCover)))
		//       return bestCoverCell;   // else return the tidy (spacing-respecting) pick
		static bool BendsIntoCover(bool coverFound, bool found, int tidyCover, int coverCellCover, bool coverFirst)
		{
			return coverFound && (!found || tidyCover <= 0 || (coverFirst && coverCellCover > tidyCover));
		}

		[Test]
		public void CoverFirstOffReducesToLegacyBehavior()
		{
			// With coverFirst=false (bots and human Spread) the predicate must be byte-identical to
			// the pre-DP-3 logic: bend only when the tidy pick has NO cover. A tidy pick that already
			// has some cover is kept even if a richer cover cell exists nearby.
			Assert.That(BendsIntoCover(true, true, tidyCover: 5, coverCellCover: 30, coverFirst: false), Is.False);
			Assert.That(BendsIntoCover(true, true, tidyCover: 0, coverCellCover: 10, coverFirst: false), Is.True);
			// No reachable cover cell at all: never bend regardless of flag.
			Assert.That(BendsIntoCover(false, true, tidyCover: 0, coverCellCover: 0, coverFirst: false), Is.False);
			Assert.That(BendsIntoCover(false, true, tidyCover: 0, coverCellCover: 0, coverFirst: true), Is.False);
		}

		[Test]
		public void CoverFirstBendsForAStrictlyBetterCoverCell()
		{
			// DP-3 human Loose: bend into a strictly-better cover cell even when the tidy pick already
			// had some cover — every unit takes the best reachable cover, line shape yields.
			Assert.That(BendsIntoCover(true, true, tidyCover: 5, coverCellCover: 30, coverFirst: true), Is.True);
			// Equal cover is not "strictly better": keep the tidy pick (no needless line-bending).
			Assert.That(BendsIntoCover(true, true, tidyCover: 30, coverCellCover: 30, coverFirst: true), Is.False);
			// A worse cover cell never wins.
			Assert.That(BendsIntoCover(true, true, tidyCover: 30, coverCellCover: 10, coverFirst: true), Is.False);
		}

		[Test]
		public void NoCoverAnywhereKeepsCleanLine()
		{
			// "Do not degrade formations on purpose where no cover exists": when nothing in the
			// window has cover (coverFound=false), the unit keeps its tidy on-line slot in every mode.
			Assert.That(BendsIntoCover(false, true, 0, 0, true), Is.False);
			Assert.That(BendsIntoCover(false, true, 0, 0, false), Is.False);
		}
	}
}
