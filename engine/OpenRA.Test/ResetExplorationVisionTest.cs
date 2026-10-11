#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// WW3MOD (2026-10-11). Pins MapLayers.ResetExploration to the counters this class actually maintains.
	///
	/// ENG #10 (DOCS/design/261007_engine-robustness-scout.md §10): ResetExploration re-derived the explored
	/// bit from upstream Shroud's visibleCount + passiveVisibleCount. MapLayers allocated both layers and
	/// never incremented either — AddSource/RemoveSource maintain visibilityCount[cell][strength] — so the
	/// sum was always 0 and every reset (DevAll toggled off, DevResetExploration, a hide-map crate) blacked
	/// out cells the player's own stationary units were looking at until a source happened to re-add.
	///
	/// MapLayers needs a live World to construct, so this pins the two halves separately: the per-cell
	/// predicate as a truth table, and by IL scan that ResetExploration is what calls it. The second half
	/// is the regression guard — a predicate nobody calls would pass the first half forever.
	/// </summary>
	[TestFixture]
	public class ResetExplorationVisionTest
	{
		static short[] Counts(params (int Level, short Count)[] set)
		{
			var counts = new short[MapLayers.VisionLayers];
			foreach (var (level, count) in set)
				counts[level] = count;
			return counts;
		}

		[Test]
		public void CellNeverTouchedHasNoVision()
		{
			// visibilityCount[index] stays null until AddSource or Tick first reaches the cell.
			Assert.That(MapLayers.HasCountedVision(null), Is.False);
			Assert.That(MapLayers.HasCountedVision(Counts()), Is.False);
		}

		[Test]
		public void AnyCountedStrengthIsVision()
		{
			// AddSource clamps modifiedStrength to >= 1, so a heavily shadowed observer lands on level 1.
			Assert.That(MapLayers.HasCountedVision(Counts((1, 1))), Is.True, "shadow-clamped observer at level 1");
			Assert.That(MapLayers.HasCountedVision(Counts((MapLayers.VisionLayers - 1, 1))), Is.True, "top level");
			Assert.That(MapLayers.HasCountedVision(Counts((4, 2), (7, 1))), Is.True, "several sources");
		}

		[Test]
		public void LayerZeroIsNotVision()
		{
			// Nothing writes level 0 (see the clamp above); Tick's highest-level search skips it too.
			// Reading it here would make ResetExploration disagree with what Tick resolves.
			Assert.That(MapLayers.HasCountedVision(Counts((0, 3))), Is.False);
		}

		[Test]
		public void RemovedSourceLeavesNoVision()
		{
			// RemoveSource decrements back to 0 rather than nulling the array.
			Assert.That(MapLayers.HasCountedVision(Counts((5, 0), (2, 0))), Is.False);
		}

		[Test]
		public void ResetExplorationDerivesExploredFromVisibilityCount()
		{
			var reset = typeof(MapLayers).GetMethod(nameof(MapLayers.ResetExploration), BindingFlags.Instance | BindingFlags.Public);
			Assert.That(reset, Is.Not.Null, "MapLayers.ResetExploration not found — renamed? Re-point this pin.");

			var scan = IlScan.Scan(reset);
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(1),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in MapLayers.ResetExploration — the scanner " +
				"is reading nothing, so the assertion below would be vacuous.");

			Assert.That(scan.Callees.Any(m => m.DeclaringType == typeof(MapLayers) && m.Name == nameof(MapLayers.HasCountedVision)),
				"MapLayers.ResetExploration no longer derives the explored bit through HasCountedVision. It must re-derive " +
				"it from visibilityCount, the only per-cell vision counter AddSource/RemoveSource maintain; reading any " +
				"other layer resets every cell to unexplored, including cells the player's own units are seeing (ENG #10).");
		}
	}
}
