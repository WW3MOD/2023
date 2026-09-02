#region Copyright & License Information
/*
 * Locks the developer overlay's default to its release paperwork, in both directions.
 *
 * DebugVisualizations.DamageNumbers now defaults FALSE, which is the shipped state. It was briefly
 * true (user ruling 2026-08-30, deferred under blocker R17) and was flipped the same day once main
 * began being pushed as work landed -- default-on would have put floating damage numbers over every
 * unit on the user's next pull, under a play-through whose purpose was filing polish items.
 *
 * THE GUARD DID NOT RETIRE WITH R17; IT REVERSED. It now stops the overlay being turned back on
 * without anyone filing a blocker for it:
 *
 *   default false + no blocker entry -> pass (today, shipped)
 *   default true  + blocker entry    -> pass (a deliberate, tracked deferral)
 *   default true  + no blocker entry -> FAIL ("file the entry or flip it back")
 *   default false + blocker entry    -> FAIL ("you flipped it, now delete the entry")
 *
 * A comment saying "do not ship this on" is not a countermeasure -- this repo has watched prose fail
 * at exactly that job more than once. The failure mode this exists to prevent is someone flipping
 * the default for a debugging session, and that flip surviving into a release because nothing
 * anywhere objected.
 *
 * The marker is matched as a WHOLE HTML COMMENT, not as a bare token, so that documentation which
 * merely mentions the token in prose -- including the note left in PIPELINE.md where R17 used to be,
 * and this very file -- does not count as the entry existing. That bit me while writing the
 * discharge note.
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
		// Matched as the whole HTML comment rather than as a bare token, so prose that merely names
		// the marker -- the discharge note in PIPELINE.md does, and so does this file -- is not
		// mistaken for the entry itself. Assembled from parts for the same reason: a literal here
		// would make THIS file match a naive grep for the entry.
		const string BlockerToken = "HITCHECK-OVERLAY-DEFAULT-ON";
		const string BlockerMarker = "<!-- " + BlockerToken + " -->";

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
					$"DebugVisualizations.DamageNumbers has been turned back ON, so the developer overlay " +
					$"ships enabled and a stranger would see floating damage numbers in their first " +
					$"firefight. That is a release blocker and it needs an entry in WORKSPACE/PIPELINE.md " +
					$"carrying the marker comment {BlockerMarker} so it is caught before release. File one " +
					$"in this commit, or flip the default back to false. Note the overlay is NOT needed to " +
					$"run the detector -- hitcheck.log is written either way; the checkbox turns the " +
					$"on-screen half on for a session without touching the default.");
			}
			else
			{
				Assert.That(entryPresent, Is.False,
					$"DebugVisualizations.DamageNumbers defaults to FALSE, so there is nothing to defer and " +
					$"no blocker entry should exist. Delete the entry carrying {BlockerMarker} from " +
					$"WORKSPACE/PIPELINE.md in this commit -- a blocker list that keeps entries after they " +
					$"are discharged stops being read.");
			}
		}

		[Test]
		public void TheDefaultIsPinnedSoFlippingItShowsUpInADiff()
		{
			// Deliberately asserts the CURRENT value rather than a desired one. Its job is to make any
			// flip a visible, deliberate edit to a test rather than a one-character change nobody
			// reviews -- in EITHER direction. It flipped once already, from True to False, when the
			// R17 deferral ran out of road on 2026-08-30.
			Assert.That(DefaultDamageNumbers(), Is.False,
				"DamageNumbers is expected to default to false: the overlay is developer-only and must not " +
				"be on in a build the user play-tests. If you are deliberately turning it on, change this " +
				"assertion to True and file the blocker entry in WORKSPACE/PIPELINE.md in the same commit. " +
				"If you only want it for one session, use the Damage Numbers checkbox instead.");
		}

		[Test]
		public void EveryDebugVisualisationStaysOff()
		{
			// No debug visualisation may default on. DamageNumbers is checked separately above
			// because it is the one that has been on before and carries the blocker machinery; the
			// rest are swept here so a second one cannot be switched on quietly without its own
			// entry and its own argument.
			var vis = new DebugVisualizations();

			Assert.That(vis.DamageNumbers, Is.False);
			Assert.That(vis.CombatGeometry, Is.False);
			Assert.That(vis.RenderGeometry, Is.False);
			Assert.That(vis.ScreenMap, Is.False);
			Assert.That(vis.ActorTags, Is.False);
			Assert.That(vis.DepthBuffer, Is.False);
		}

		/// <summary>
		/// Walks up from the test binary looking for WORKSPACE/PIPELINE.md.
		///
		/// FRAGILITY, written here because it is invisible from the assertion that depends on it:
		/// this fixture is coupled to that FILE PATH. If PIPELINE.md is renamed, moved, or split the
		/// way it was already split into pipeline/items/ during 2026-08, this throws and the failure
		/// reads like a broken test rather than like the blocker-tracking mechanism it is. The fix
		/// then is to repoint the path, NOT to delete the fixture -- deleting it silently un-guards a
		/// debug overlay that ships ON by default.
		///
		/// Deliberate trade: coupling to a path is what buys the bidirectional lock between the code
		/// default and the release paperwork, and there is nowhere else that lock could live.
		/// </summary>
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

			throw new FileNotFoundException(
				"could not locate WORKSPACE/PIPELINE.md -- if it moved, repoint this test rather than " +
				"deleting it: it is the guard on a debug overlay that ships default ON");
		}
	}
}
