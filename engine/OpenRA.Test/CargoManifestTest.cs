#region Copyright & License Information
/*
 * WW3MOD cargo manifest tests — the rows the sidebar panel and the unload menu are both built from.
 *
 * These pins exist because the failure they guard is INVISIBLE BY CONSTRUCTION. A transport whose
 * classes outnumber the panel's slots has to say so; if it simply stops drawing at the last slot,
 * the panel is not wrong-looking, it is confidently short — and a screenshot of it is indistinguish-
 * able from a correct one, because nothing on screen claims the list is complete. That exact shape
 * already shipped once here: the unload menu's fixed 16-row cap drew rows 17..24 nowhere with
 * `ScrollBar: Hidden` to make sure nobody could tell (CargoUnloadMenuLogic.cs:175-179).
 *
 * Asserted on the pure row list rather than on live widget children ON PURPOSE. A count taken off
 * ScrollPanelWidget.Children reads the same on a truncating build as on a correct one, because
 * Refresh adds every row before anything sizes or clips it — so `Children.Count == 24` passes
 * against the defect it was written to catch.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class CargoManifestTest
	{
		static List<CargoManifestRow> Groups(int count, int perGroup = 1)
		{
			return Enumerable.Range(0, count)
				.Select(i => new CargoManifestRow($"key{i}", $"Class {i}", perGroup))
				.ToList();
		}

		// Grouping keys on Selectable.Class so veteran variants fold into their base row. Falling back
		// to the actor name matters for the passengers that have no class at all — civilians, pilots,
		// ejected vehicle crews — which would otherwise share one empty key and merge into each other.
		[Test]
		public void ClasslessPassengersGroupByActorNameRatherThanMerging()
		{
			Assert.That(CargoManifest.GroupKey("rifleman", "e1r1"), Is.EqualTo("rifleman"));
			Assert.That(CargoManifest.GroupKey("", "c1"), Is.EqualTo("c1"));
			Assert.That(CargoManifest.GroupKey(null, "pilot"), Is.EqualTo("pilot"));
			Assert.That(CargoManifest.GroupKey("", "c1"), Is.Not.EqualTo(CargoManifest.GroupKey("", "c2")),
				"two classless passenger types must not collapse into one row");
		}

		[Test]
		public void ListsThatFitAreLeftAlone()
		{
			var rows = CargoManifest.Fit(Groups(4), 10);
			Assert.That(rows.Count, Is.EqualTo(4));
			Assert.That(rows.Select(r => r.Label), Is.EqualTo(new[] { "Class 0", "Class 1", "Class 2", "Class 3" }));
		}

		[Test]
		public void AListThatExactlyFillsTheSlotsGetsNoOverflowRow()
		{
			var rows = CargoManifest.Fit(Groups(10), 10);
			Assert.That(rows.Count, Is.EqualTo(10));
			Assert.That(rows.Last().Label, Is.EqualTo("Class 9"),
				"ten classes in ten slots all fit — spending the last slot on an overflow row would hide one that did fit");
		}

		// THE PIN. A 36-slot Chinook takes `Types: Infantry`, which admits 24 distinct classes. Ten
		// slots cannot show them, and the only unacceptable outcome is showing ten and implying that
		// is all of them.
		[Test]
		public void MoreClassesThanSlotsSaysSoInsteadOfTruncatingSilently()
		{
			var rows = CargoManifest.Fit(Groups(24, 2), 10);

			Assert.That(rows.Count, Is.EqualTo(10), "must not overrun the slots the panel actually has");
			Assert.That(rows.Last().Label, Is.EqualTo("+15 more"),
				"the last slot must declare the classes it is standing in for; without it the panel " +
				"silently claims a 24-class transport holds only the 10 it had room to draw");
			Assert.That(rows.Last().Count, Is.EqualTo(30),
				"the overflow row counts the men it hides, not the rows, so the count column stays in one unit");
			Assert.That(rows.Take(9).Select(r => r.Label), Is.EqualTo(new[]
			{
				"Class 0", "Class 1", "Class 2", "Class 3", "Class 4", "Class 5", "Class 6", "Class 7", "Class 8"
			}));
		}

		// Degenerate but reachable: one slot and several classes. The single row must be the overflow
		// marker, never class 0 posing as the whole manifest.
		[Test]
		public void ASingleSlotSpendsItselfOnTheOverflowMarker()
		{
			var rows = CargoManifest.Fit(Groups(3, 4), 1);
			Assert.That(rows.Count, Is.EqualTo(1));
			Assert.That(rows[0].Label, Is.EqualTo("+3 more"));
			Assert.That(rows[0].Count, Is.EqualTo(12));
		}

		[Test]
		public void EmptyAndDegenerateInputsAreInert()
		{
			Assert.That(CargoManifest.Fit(new List<CargoManifestRow>(), 10), Is.Empty);
			Assert.That(CargoManifest.Fit(Groups(4), 0), Is.Empty);
			Assert.That(CargoManifest.Fit(null, 10), Is.Empty);
		}
	}
}
