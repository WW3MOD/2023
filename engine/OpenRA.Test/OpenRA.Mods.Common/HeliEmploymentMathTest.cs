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

		/// <summary>Readability wrapper over the production signature, defaulting the ammo and health
		/// arguments to their FROZEN values — full ammo, full health, no repair host, and both new levers
		/// OFF — so the pre-existing cases below still read as the scenarios they were written for.
		/// The production call site does NOT get defaults: every parameter of HeliEmploymentMath.Decide is
		/// required precisely so the compiler, not a test, guarantees the module passes them.</summary>
		static HeliDisposition Decide(
			bool hasUsableAmmo, bool canRearm, bool hasWorthwhileTarget,
			bool contactEverObserved, bool nearHome, int idleTicks, int evacuateIdleTicks,
			bool evacuateForwardIdle = false,
			int ammoPercent = 100, int evacuateAmmoPercent = 0,
			bool canRepair = false, int healthPercent = 100, int evacuateBelowHealthPercent = 0)
		{
			return HeliEmploymentMath.Decide(
				hasUsableAmmo, canRearm, hasWorthwhileTarget, contactEverObserved, nearHome, idleTicks,
				evacuateIdleTicks, evacuateForwardIdle, ammoPercent, evacuateAmmoPercent,
				canRepair, healthPercent, evacuateBelowHealthPercent);
		}

		[Test]
		public void SpentAndCannotRearm_EvacuatesRegardlessOfEverythingElse()
		{
			// No ammo + no rearm host ⇒ evac even with a live target, forward, inside the window, and BEFORE
			// first contact (rule 1 is unconditional — a disarmed heli has no value regardless of phase).
			Assert.That(
				Decide(
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
				Decide(
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
				Decide(
					hasUsableAmmo: false, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.Evacuate));
		}

		[Test]
		public void NoTargetAtHomePastWindow_AfterContact_Evacuates()
		{
			Assert.That(
				Decide(
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
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: false, nearHome: true, idleTicks: Window * 4, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void NoTargetAtHomeWithinWindow_Holds()
		{
			// One tick short of the window ⇒ still holding (give the mission loop a chance to form).
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: true, idleTicks: Window - 1, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void HasWorthwhileTarget_HoldsEvenAtHomePastWindow()
		{
			// A believed target keeps the heli committed to the mission loop rather than evacuating.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: Window * 4, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void NoTargetButForward_Holds()
		{
			// Idle with no target but positioned forward (not near home) ⇒ left to the squad FSM, not evac'd.
			// Default (EvacuateForwardIdle off) preserves the frozen forward-hold behaviour.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: false, idleTicks: Window * 4, evacuateIdleTicks: Window),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void NoTargetButForward_WithForwardEvac_Evacuates()
		{
			// Mission-complete evac (Item C): the SAME forward, target-less, past-window heli now evacuates
			// when EvacuateForwardIdle is set — it does not loiter at the front with no follow-up mission.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: false, idleTicks: Window * 4, evacuateIdleTicks: Window,
					evacuateForwardIdle: true),
				Is.EqualTo(HeliDisposition.Evacuate));
		}

		[Test]
		public void NoTargetButForward_WithForwardEvac_WithinWindow_Holds()
		{
			// The patience window still applies with forward evac on: one tick short ⇒ still holding, so a
			// heli briefly idle between engagements at the front is not dumped.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: true, nearHome: false, idleTicks: Window - 1, evacuateIdleTicks: Window,
					evacuateForwardIdle: true),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void HasTargetForward_WithForwardEvac_Holds()
		{
			// A believed target keeps a forward heli committed even with forward evac enabled — evac is only
			// for the genuinely-nothing-to-do case.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: false, idleTicks: Window * 4, evacuateIdleTicks: Window,
					evacuateForwardIdle: true),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void NoTargetForward_WithForwardEvac_BeforeFirstContact_Holds()
		{
			// The first-contact gate still applies: an anticipatory forward heli before any believed enemy is
			// held (staged), not evac'd/re-bought, even with forward evac enabled.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: false,
					contactEverObserved: false, nearHome: false, idleTicks: Window * 4, evacuateIdleTicks: Window,
					evacuateForwardIdle: true),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		// ---------- Low-ammo evacuation (C1 / P12) ----------
		//
		// The gap this closes: every other ammo test in the tree fires at absolute zero. HasUsableAmmo is true
		// while ANY pool holds a round, AirframeEvacMath returns None while loadedPools > 0, and
		// SendDryUnitsHome's predicate is !HasAmmo. So a gunship with one rocket left and no rearm host was
		// treated as armed. Each case below is chosen so the OLD code — which had no ammo-percent branch at
		// all — returns HoldForMission, which is what makes them a RED test rather than a restatement.

		[Test]
		public void LowAmmoWithNoRearmHost_Evacuates()
		{
			// 30% of a full load, no rearm host, and STILL HAS AMMO — so the pre-existing !hasUsableAmmo branch
			// cannot be what fires. A believed target and a fresh idle counter are both set so no other branch
			// can either: this can only be the new low-ammo rule.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					ammoPercent: 30, evacuateAmmoPercent: 34),
				Is.EqualTo(HeliDisposition.Evacuate));
		}

		[Test]
		public void LowAmmoButCanRearm_Holds()
		{
			// The asymmetry that keeps this from scrapping recoverable value: if it can refill, refilling beats
			// scrapping. Same 30% against the same threshold; only canRearm differs.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: true, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					ammoPercent: 30, evacuateAmmoPercent: 34),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void AmmoAboveThreshold_Holds()
		{
			// One point above the bar ⇒ still a fighting airframe. Pins the boundary from the other side.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					ammoPercent: 35, evacuateAmmoPercent: 34),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void AmmoExactlyAtThreshold_Evacuates()
		{
			// At-or-below, not strictly-below. Worth a test because the two differ by one salvo and the YAML
			// comment promises "roughly one salvo left".
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					ammoPercent: 34, evacuateAmmoPercent: 34),
				Is.EqualTo(HeliDisposition.Evacuate));
		}

		[Test]
		public void LowAmmoLeverOff_Holds_FrozenDefault()
		{
			// THE OFF-SWITCH CONTRACT. evacuateAmmoPercent: 0 is what @stable and every non-opted-in profile
			// runs, and at 1% ammo with no rearm host it must still reach the answer the old code did. Without
			// this, a mis-specified "0 means always" would silently retire @stable's entire air arm.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: false, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					ammoPercent: 1, evacuateAmmoPercent: 0),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		// ---------- Damaged-airframe evacuation (C2 / P13) ----------

		[Test]
		public void DamagedWithNoRepairHost_Evacuates()
		{
			// Full ammo, live target, fresh idle counter — every other branch is held open deliberately, so a
			// pass here can only be the health rule. The refund is HP-scaled, so this airframe's recoverable
			// value only falls from here.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: true, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					canRepair: false, healthPercent: 30, evacuateBelowHealthPercent: 35),
				Is.EqualTo(HeliDisposition.Evacuate));
		}

		[Test]
		public void DamagedButCanRepair_Holds()
		{
			// Capturing a repair host must make the bot MEND rather than scrap. Same 30% health; only canRepair
			// differs, so this pins that the gate is on the host and not on the health alone.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: true, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					canRepair: true, healthPercent: 30, evacuateBelowHealthPercent: 35),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void HealthAboveThreshold_Holds()
		{
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: true, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					canRepair: false, healthPercent: 36, evacuateBelowHealthPercent: 35),
				Is.EqualTo(HeliDisposition.HoldForMission));
		}

		[Test]
		public void DamagedLeverOff_Holds_FrozenDefault()
		{
			// The other off-switch contract: at 1% health with no repair host, a profile that has not opted in
			// keeps the old answer. This is the one that would have moved @stable if the default were wrong.
			Assert.That(
				Decide(
					hasUsableAmmo: true, canRearm: true, hasWorthwhileTarget: true,
					contactEverObserved: true, nearHome: true, idleTicks: 0, evacuateIdleTicks: Window,
					canRepair: false, healthPercent: 1, evacuateBelowHealthPercent: 0),
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
				Decide(true, false, false, true, true, Window, Window),
				Is.EqualTo(Decide(true, false, false, true, true, Window, Window)));
		}
	}
}
