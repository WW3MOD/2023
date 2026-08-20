#region Copyright & License Information
/*
 * WW3MOD garrison exit-cell tests — which door a force-moved garrison soldier walks out of.
 *
 * The behaviour under test: ordered out of a building, one soldier leaves on the side he was sent
 * toward, and a blocked door does not turn the order into nothing.
 *
 * WHAT THESE PIN, and what they cannot. Like GarrisonPortSwapTest, these sit on the pure helper
 * because the caller needs an Actor, a World and a loaded ruleset, and Actor has no accessible
 * constructor from this assembly. So this file pins the CHOICE only; that the choice is correctly
 * WIRED — that a ForceMove on a garrisoned soldier releases exactly him and leaves the rest of the
 * garrison inside — is the Lua scenario's job (tools/autotest/scenarios/test-garrison-force-move-eject).
 * Neither half is evidence without the other.
 *
 * HALF OF THIS FILE EXISTS TO FAIL THE OBVIOUS WRONG IMPLEMENTATION. "Put him on a free cell next to
 * the building" is the shape the code naturally wants to take, and it is what Cargo's unload already
 * does — ChooseExitSubCell takes a SharedRandom-shuffled pick of the adjacent ring. A helper that
 * returns any free neighbour satisfies "he got out" perfectly well and fails the actual request,
 * which is that the direction is the player's, not the shuffle's. TheChoiceIsNotSimplyTheFirstFree
 * and the two directional tests go red under it.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class GarrisonExitMathTest
	{
		// A one-cell building at (5,5) and the eight cells around it, listed anticlockwise from the
		// north-west so that no test can pass merely by taking the head of the sequence.
		static readonly CPos[] AdjacentRing =
		{
			new CPos(4, 4), new CPos(4, 5), new CPos(4, 6), new CPos(5, 6),
			new CPos(6, 6), new CPos(6, 5), new CPos(6, 4), new CPos(5, 4)
		};

		static CPos? Choose(CPos destination, params CPos[] blocked)
		{
			var blockedSet = new HashSet<CPos>(blocked);
			return GarrisonExitMath.ChooseExitCell(AdjacentRing, destination, c => !blockedSet.Contains(c));
		}

		// The instruction, in one assertion: sent east, he steps out of the east door.
		[Test]
		public void HeLeavesOnTheSideHeWasSentToward()
		{
			Assert.That(Choose(new CPos(20, 5)), Is.EqualTo(new CPos(6, 5)),
				"a soldier sent to a destination due east must leave by the east cell");
		}

		// The mirror, so a helper that happens to favour one axis cannot pass the pair.
		[Test]
		public void TheOppositeOrderUsesTheOppositeDoor()
		{
			Assert.That(Choose(new CPos(-10, 5)), Is.EqualTo(new CPos(4, 5)),
				"a soldier sent due west must leave by the west cell, not the east one");
		}

		// The control against "return the first free neighbour". That implementation returns (4,4),
		// the head of the ring, for every destination on the map and passes any test that only asks
		// whether he got out at all.
		[Test]
		public void TheChoiceIsNotSimplyTheFirstFreeCandidate()
		{
			Assert.That(Choose(new CPos(20, 5)), Is.Not.EqualTo(AdjacentRing[0]),
				"the exit cell must be chosen from the destination, not taken off the front of the candidate list");
		}

		// A building backed against a cliff or crowded by its own garrison must not swallow the order.
		// The nearest door is gone, so he takes the next-nearest one rather than nothing.
		[Test]
		public void ABlockedDoorFallsBackToTheNextOpening()
		{
			Assert.That(Choose(new CPos(20, 5), new CPos(6, 5)), Is.EqualTo(new CPos(6, 4)),
				"with the east cell blocked he must still leave, by the next-closest free cell");
		}

		// Only when there is genuinely nowhere to stand does the helper decline to place him. The
		// caller reads this as "release him onto the building cell anyway", never as "do nothing".
		[Test]
		public void NoFreeCellIsNullRatherThanAnArbitraryOne()
		{
			Assert.That(GarrisonExitMath.ChooseExitCell(AdjacentRing, new CPos(20, 5), _ => false), Is.Null);
		}

		// Order resolution runs on every client, so the answer may not depend on how the candidate
		// sequence was assembled. (6,4) and (6,6) are equidistant from (20,5); the tie must land the
		// same way whichever is enumerated first.
		[Test]
		public void EquidistantDoorsAreSettledIndependentlyOfCandidateOrder()
		{
			var blocked = new HashSet<CPos> { new CPos(6, 5) };
			bool IsFree(CPos c) => !blocked.Contains(c);

			var forward = GarrisonExitMath.ChooseExitCell(AdjacentRing, new CPos(20, 5), IsFree);
			var reversed = GarrisonExitMath.ChooseExitCell(AdjacentRing.Reverse(), new CPos(20, 5), IsFree);

			Assert.That(forward, Is.EqualTo(reversed),
				"two clients enumerating the adjacent ring differently would put the same soldier on " +
				"different cells, which is a desync rather than a cosmetic disagreement");
		}

		// Degenerate but reachable: force-moving onto the building the soldier is standing in. Every
		// candidate is equidistant, so this is purely a "does it stay deterministic and not throw" pin.
		[Test]
		public void AnOrderOntoTheBuildingItselfStillResolves()
		{
			Assert.That(Choose(new CPos(5, 5)), Is.Not.Null);
		}
	}
}
