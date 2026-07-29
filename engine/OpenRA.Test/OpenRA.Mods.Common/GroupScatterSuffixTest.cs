#region Copyright & License Information
/*
 * WW3MOD Group Scatter (Shift-G) common-suffix contract test.
 *
 * Pins the pure geometry that decides which queued orders a group-scatter redistributes: the LONGEST
 * COMMON SUFFIX across the selected units' order chains (the shared group-orders) is the only part
 * that moves; everything ahead of it is each unit's unique prefix and must be left intact. This is the
 * contract test-spread-preserves-prefix asserts at the behavioural level — see
 * WORKSPACE/plans/260729_spread_prefix_brief.md.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets.Logic.Ingame;

namespace OpenRA.Test
{
	[TestFixture]
	public class GroupScatterSuffixTest
	{
		static CPos C(int x, int y) => new(x, y);

		static IReadOnlyList<(CPos, string)> Chain(params (int X, int Y, string Order)[] wps)
			=> wps.Select(w => (C(w.X, w.Y), w.Order)).ToList();

		static int Suffix(params IReadOnlyList<(CPos, string)>[] chains)
			=> GroupScatterHotkeyLogic.CommonSuffixLength(chains.ToList());

		[Test]
		public void EmptyAndDegenerateInputs()
		{
			Assert.Multiple(() =>
			{
				Assert.That(GroupScatterHotkeyLogic.CommonSuffixLength(null), Is.EqualTo(0), "null -> 0");
				Assert.That(GroupScatterHotkeyLogic.CommonSuffixLength(
					new List<IReadOnlyList<(CPos, string)>>()), Is.EqualTo(0), "no chains -> 0");

				// A single participant's whole chain is trivially its own common suffix.
				Assert.That(Suffix(Chain((8, 10, "Move"), (20, 11, "AttackMove"))), Is.EqualTo(2),
					"single chain -> full length");

				// One empty chain among others collapses the suffix to 0 (min length is 0).
				Assert.That(Suffix(Chain((8, 10, "Move")), Chain()), Is.EqualTo(0),
					"an empty chain forces suffix 0");
			});
		}

		[Test]
		public void IdenticalChainsShareTheWholeChain()
		{
			// The common basic case: a group order queued on the whole selection — every unit holds the
			// same chain, so the entire thing is the shared suffix (no unique prefix to preserve).
			var a = Chain((28, 13, "Move"), (28, 19, "Move"));
			var b = Chain((28, 13, "Move"), (28, 19, "Move"));
			var c = Chain((28, 13, "Move"), (28, 19, "Move"));
			Assert.That(Suffix(a, b, c), Is.EqualTo(2), "identical chains -> full common suffix");
		}

		[Test]
		public void UniquePrefixWithSharedAttackMoveSuffix()
		{
			// The test-spread-preserves-prefix scenario: each tank has a UNIQUE prefix Move then the SAME
			// two group AttackMoves queued behind it. Only the 2-long AttackMove suffix is shared.
			var tankA = Chain((8, 10, "Move"), (20, 11, "AttackMove"), (20, 13, "AttackMove"));
			var tankB = Chain((8, 14, "Move"), (20, 11, "AttackMove"), (20, 13, "AttackMove"));
			Assert.That(Suffix(tankA, tankB), Is.EqualTo(2),
				"divergent prefix Move + shared AttackMove pair -> suffix of 2");
		}

		[Test]
		public void FullyDivergentChainsHaveNoSharedSuffix()
		{
			// No group order was ever queued — the two units hold entirely different single orders. There
			// is nothing to preserve-vs-redistribute, so the caller falls back to legacy aggregation.
			var a = Chain((8, 10, "Move"));
			var b = Chain((8, 14, "Move"));
			Assert.That(Suffix(a, b), Is.EqualTo(0), "no shared trailing order -> 0");
		}

		[Test]
		public void SuffixIsBoundedByShortestChainAndStopsAtFirstMismatch()
		{
			Assert.Multiple(() =>
			{
				// Shared tail is [Move(20,13)] only: the (20,11) waypoint is present in the longer chain but
				// the shorter chain's matching-from-the-end position holds a different cell.
				var longChain = Chain((8, 10, "Move"), (20, 11, "AttackMove"), (20, 13, "AttackMove"));
				var shortChain = Chain((20, 11, "AttackMove"), (20, 13, "AttackMove"));
				Assert.That(Suffix(longChain, shortChain), Is.EqualTo(2),
					"suffix capped at the shorter chain's length when the tail matches");

				// A mid-suffix divergence stops the run even though the very last cells match.
				var x = Chain((1, 1, "Move"), (5, 5, "AttackMove"), (9, 9, "Move"));
				var y = Chain((2, 2, "Move"), (6, 6, "AttackMove"), (9, 9, "Move"));
				Assert.That(Suffix(x, y), Is.EqualTo(1), "matching last cell but divergent second-last -> 1");
			});
		}

		[Test]
		public void OrderTypeDistinguishesOtherwiseIdenticalCells()
		{
			// Same cell, different order kind (a Move vs an AttackMove to the same tile) is NOT a shared
			// waypoint — the suffix must compare BOTH cell and order type.
			var a = Chain((7, 7, "Move"), (20, 20, "Move"));
			var b = Chain((7, 7, "Move"), (20, 20, "AttackMove"));
			Assert.That(Suffix(a, b), Is.EqualTo(0), "trailing cell matches but order type differs -> 0");
		}
	}
}
