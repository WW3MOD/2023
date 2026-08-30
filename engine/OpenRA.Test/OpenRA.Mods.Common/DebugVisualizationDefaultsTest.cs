#region Copyright & License Information
/*
 * Guards the one debug default that ships ON.
 *
 * DebugVisualizations.DamageNumbers defaults to true so the developer overlay is visible without
 * anyone having to switch it on (user ruling 2026-08-30). That makes it a RELEASE BLOCKER: a
 * stranger's first firefight would show floating damage numbers over every unit.
 *
 * A comment saying "flip this before release" is not a countermeasure -- this repo has watched
 * prose fail at exactly that job more than once. So the guard is mechanical and BIDIRECTIONAL: the
 * code default and the PIPELINE.md blocker entry are locked to each other, and breaking either
 * direction fails the build.
 *
 *   default true  + no blocker entry -> FAIL ("the entry is load-bearing, put it back")
 *   default false + blocker entry    -> FAIL ("you flipped it, now delete the entry")
 *   default true  + blocker entry    -> pass (today)
 *   default false + no blocker entry -> pass (shipped state)
 *
 * The failure mode this exists to prevent is not someone forgetting to flip the bool. It is someone
 * tidying the blocker list, deleting the entry because it looks like stale paperwork, and leaving
 * the overlay on with nothing anywhere to catch it.
 */
#endregion

using System;
using System.IO;
using NUnit.Framework;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DebugVisualizationDefaultsTest
	{
		// Written into the R17 entry as an HTML comment so it survives prose edits to the entry but
		// disappears when the entry itself is deleted.
		const string BlockerMarker = "HITCHECK-OVERLAY-DEFAULT-ON";

		static bool DefaultDamageNumbers()
		{
			return new DebugVisualizations().DamageNumbers;
		}

		[Test]
		public void TheOverlayDefaultAndTheReleaseBlockerEntryAgree()
		{
			var defaultOn = DefaultDamageNumbers();
			var pipeline = File.ReadAllText(FindPipeline());
			var entryPresent = pipeline.Contains(BlockerMarker, StringComparison.Ordinal);

			if (defaultOn)
			{
				Assert.That(entryPresent, Is.True,
					$"DebugVisualizations.DamageNumbers still defaults to TRUE, so the developer overlay " +
					$"ships on and a stranger sees floating damage numbers in their first firefight. The " +
					$"'{BlockerMarker}' entry in WORKSPACE/PIPELINE.md is the thing that gets that caught " +
					$"before release -- it is load-bearing, not stale paperwork. Put it back, or flip the " +
					$"default to false in the same commit.");
			}
			else
			{
				Assert.That(entryPresent, Is.False,
					$"DebugVisualizations.DamageNumbers now defaults to FALSE, so the release blocker is " +
					$"discharged. Delete the '{BlockerMarker}' entry (R17) from WORKSPACE/PIPELINE.md in " +
					$"this commit -- a blocker list that keeps entries after they are fixed stops being " +
					$"read.");
			}
		}

		[Test]
		public void TheDefaultIsPinnedSoFlippingItShowsUpInADiff()
		{
			// Deliberately asserts the CURRENT value rather than the desired one. Its whole job is to
			// make the flip a visible, deliberate edit to a test rather than a one-character change
			// nobody reviews. When the default flips, this assertion flips with it, in the same commit.
			Assert.That(DefaultDamageNumbers(), Is.True,
				"DamageNumbers is expected to still default to true at this point in the project. If you " +
				"have just switched it off for release, change this assertion to False and delete the R17 " +
				"entry from WORKSPACE/PIPELINE.md.");
		}

		[Test]
		public void EveryOtherDebugVisualisationStaysOff()
		{
			// The point of DamageNumbers being separate is that it is the ONLY one on. If a second
			// overlay ever defaults on, it needs its own blocker entry and its own argument.
			var vis = new DebugVisualizations();

			Assert.That(vis.CombatGeometry, Is.False);
			Assert.That(vis.RenderGeometry, Is.False);
			Assert.That(vis.ScreenMap, Is.False);
			Assert.That(vis.ActorTags, Is.False);
			Assert.That(vis.DepthBuffer, Is.False);
		}

		static string FindPipeline()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "WORKSPACE", "PIPELINE.md");
				if (File.Exists(candidate))
					return candidate;

				dir = dir.Parent;
			}

			throw new FileNotFoundException("could not locate WORKSPACE/PIPELINE.md");
		}
	}
}
