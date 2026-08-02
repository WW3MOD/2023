#region Copyright & License Information
/*
 * WW3MOD @experimental helicopter mission-employment decision test.
 *
 * Pins the two decisions HeliEmploymentMath turns idle-heli state into:
 *   (1) DISPOSITION — an idle attack heli is EVACUATED to reserves when it is spent with no rearm
 *       host (no combat value left), or when it has loitered at home past the patience window with no
 *       believed worthwhile target (bank the money + stop upkeep instead of corner-parking); otherwise
 *       it is HELD for the squad mission loop. A believed target, forward position, remaining ammo, or a
 *       reachable rearm host each keep it held.
 *   (2) TARGET RANGE — the believed-contact-within-mission-range geometry that gates (1).
 * Pure integer math; no world mounted; deterministic.
 */
#endregion

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class HeliEmploymentMathTest
	{
		const int Window = 500;

		[Test]
		public void SpentAndCannotRearm_EvacuatesRegardlessOfEverythingElse()
		{
			// No ammo + no rearm host ⇒ evac even with a live target, forward, inside the window, and BEFORE
			// first contact (rule 1 is unconditional — a disarmed heli has no value regardless of phase).
			Assert.That(
				HeliEmploymentMath.Decide(
					hasUsableAmmo: false, canRearm: false, hasWorthwhileTarget: true,
					contactEverObserved: false, nearHome: false, idleTicks: 0, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.Evacuate));
		}

		[Test]
		public void SpentButCanRearm_NotEvacuatedImmediately()
		{
			// Out of ammo but a rearm host exists ⇒ NOT dumped on the spot (rule 1 needs !canRearm). It only
			// leaves later via the ordinary no-target-past-window path, so within the window it still holds.
			Assert.That(
				HeliEmploymentMath.Decide(
					hasUsableAmmo: false, canRearm: true, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void SpentAndCannotRearm_EvacuatesImmediatelyWithinWindow()
		{
			// The distinguishing case for canRearm: spent + NO rearm host ⇒ evac at once (idleTicks well
			// inside the window), whereas SpentButCanRearm above waits.
			Assert.That(
				HeliEmploymentMath.Decide(
					hasUsableAmmo: false, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.Evacuate));
		}

		[Test]
		public void NoTargetAtHomePastWindow_AfterContact_Evacuates()
		{
			Assert.That(
				HeliEmploymentMath.Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: true, idleTicks: Window, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.Evacuate));
		}

		[Test]
		public void NoTargetAtHomePastWindow_BeforeFirstContact_Holds()
		{
			// The first-contact gate: identical to the evac case above but no enemy has EVER been believed,
			// so an anticipatory heli is held (staged forward) rather than evac'd and re-bought pre-contact.
			Assert.That(
				HeliEmploymentMath.Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: false, nearHome: true, idleTicks: Window * 4, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void NoTargetAtHomeWithinWindow_Holds()
		{
			// One tick short of the window ⇒ still holding (give the mission loop a chance to form).
			Assert.That(
				HeliEmploymentMath.Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: true, idleTicks: Window - 1, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void HasWorthwhileTarget_HoldsEvenAtHomePastWindow()
		{
			// A believed target keeps the heli committed to the mission loop rather than evacuating.
			Assert.That(
				HeliEmploymentMath.Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: Window * 4, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void NoTargetButForward_Holds()
		{
			// Idle with no target but positioned forward (not near home) ⇒ left to the squad FSM, not evac'd.
			Assert.That(
				HeliEmploymentMath.Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: false, idleTicks: Window * 4, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void AnyTargetWithin_TrueInRange_FalseOutOfRange()
		{
			var heli = new CPos(0, 0);
			var maxRangeSq = 10L * 10L; // 10 map cells.

			var inRange = new List<CPos> { new CPos(100, 100), new CPos(6, 0) };  // second: 36 <= 100
			var outOfRange = new List<CPos> { new CPos(20, 0), new CPos(0, 15) };  // 400, 225 both > 100

			Assert.Multiple(() =>
			{
				Assert.That(HeliEmploymentMath.AnyTargetWithin(heli, inRange, maxRangeSq), Is.True);
				Assert.That(HeliEmploymentMath.AnyTargetWithin(heli, outOfRange, maxRangeSq), Is.False);
				Assert.That(HeliEmploymentMath.AnyTargetWithin(heli, new List<CPos>(), maxRangeSq), Is.False,
					"no believed contacts ⇒ no worthwhile target");
			});
		}

		[Test]
		public void AnyTargetWithin_BoundaryIsInclusive()
		{
			var heli = new CPos(0, 0);
			// Exactly on the range boundary: 8^2 + 6^2 = 100 == maxRangeSq ⇒ in range.
			Assert.That(
				HeliEmploymentMath.AnyTargetWithin(heli, new List<CPos> { new CPos(8, 6) }, 100L),
				Is.True);
		}

		[Test]
		public void DecisionsAreDeterministic()
		{
			Assert.That(
				HeliEmploymentMath.Decide(true, false, false, true, true, Window, Window),
				Is.EqualTo(HeliEmploymentMath.Decide(true, false, false, true, true, Window, Window)));
		}
	}
}
