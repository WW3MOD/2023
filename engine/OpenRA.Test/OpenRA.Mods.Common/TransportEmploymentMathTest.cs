#region Copyright & License Information
/*
 * WW3MOD — transport-helicopter employment math pin (@experimental, default-off).
 *
 * Pins TransportEmploymentMath, the decision surface behind the three transport behaviours:
 * demand-gated purchase, transports-first employment, and terminal use-or-evac. World-free and
 * zero RNG — this pins the ON-path determinism the byte-identity argument rests on
 * (see influence-stack.md Invariants).
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TransportEmploymentMathTest
	{
		// ===== Demand gate (behaviour a) =====

		[Test]
		public void NoWaitingInfantryMeansNoDemand()
		{
			Assert.That(TransportEmploymentMath.HasLiftDemand(0, 4), Is.False,
				"nobody waiting for a lift is not demand");
		}

		[Test]
		public void PartialLoadIsNotDemand()
		{
			Assert.That(TransportEmploymentMath.HasLiftDemand(3, 4), Is.False,
				"a stray squad below the minimum load does not justify a 2000-credit airframe");
		}

		[Test]
		public void FullMinimumLoadIsDemand()
		{
			Assert.That(TransportEmploymentMath.HasLiftDemand(4, 4), Is.True,
				"exactly the minimum load is demand");
			Assert.That(TransportEmploymentMath.HasLiftDemand(9, 4), Is.True,
				"more than the minimum is demand");
		}

		[Test]
		public void MinPassengersIsFlooredAtOne()
		{
			Assert.That(TransportEmploymentMath.HasLiftDemand(1, 0), Is.True,
				"a nonsensical zero threshold must not make demand unconditionally true for zero candidates");
			Assert.That(TransportEmploymentMath.HasLiftDemand(0, 0), Is.False,
				"zero candidates is never demand, whatever the configured threshold");
		}

		[Test]
		public void PurchaseIsRefusedWithoutDemand()
		{
			// Owned 0, cap 2, no idle, nobody waiting.
			Assert.That(TransportEmploymentMath.EvaluatePurchase(0, 2, 0, 0, 4),
				Is.EqualTo(TransportPurchaseDecision.NoDemand),
				"the demand gate is mandatory — this is the bought-and-idle bug");
			Assert.That(TransportEmploymentMath.ShouldBuy(0, 2, 0, 0, 4), Is.False);
		}

		[Test]
		public void PurchaseIsAuthorisedOnUnmetDemand()
		{
			Assert.That(TransportEmploymentMath.EvaluatePurchase(0, 2, 0, 4, 4),
				Is.EqualTo(TransportPurchaseDecision.Buy),
				"real demand with nothing owned to serve it authorises a call-in");
			Assert.That(TransportEmploymentMath.ShouldBuy(0, 2, 0, 4, 4), Is.True);
		}

		// ===== Transports-first (behaviour b) =====

		[Test]
		public void IdleTransportOutranksBuyingAnother()
		{
			// Demand IS present, but we already own an employable airframe.
			Assert.That(TransportEmploymentMath.EvaluatePurchase(1, 2, 1, 8, 4),
				Is.EqualTo(TransportPurchaseDecision.IdleTransportAvailable),
				"employ what we own before spending — transports-first");
			Assert.That(TransportEmploymentMath.ShouldBuy(1, 2, 1, 8, 4), Is.False);
		}

		[Test]
		public void BusyTransportDoesNotBlockBuyingForUnmetDemand()
		{
			// One owned but NOT idle (mid-delivery), demand still unserved, headroom under the cap.
			Assert.That(TransportEmploymentMath.EvaluatePurchase(1, 2, 0, 4, 4),
				Is.EqualTo(TransportPurchaseDecision.Buy),
				"transports-first keys on IDLE capacity, not merely owned capacity");
		}

		[Test]
		public void CapBindsBeforeEveryOtherReason()
		{
			// At cap with demand and no idle transport — the ceiling still wins.
			Assert.That(TransportEmploymentMath.EvaluatePurchase(2, 2, 0, 12, 4),
				Is.EqualTo(TransportPurchaseDecision.AtCap),
				"a hard ceiling can never be spent past");
			Assert.That(TransportEmploymentMath.EvaluatePurchase(3, 2, 0, 12, 4),
				Is.EqualTo(TransportPurchaseDecision.AtCap),
				"already over the ceiling stays refused");

			// PRECEDENCE: cap outranks transports-first. Both reasons apply here (at cap AND an idle
			// transport is available); AtCap must be the reported one, or the ordering in EvaluatePurchase
			// could be silently reversed without a failing test.
			Assert.That(TransportEmploymentMath.EvaluatePurchase(2, 2, 1, 12, 4),
				Is.EqualTo(TransportPurchaseDecision.AtCap),
				"cap is checked before transports-first");

			// PRECEDENCE: cap outranks the demand gate too (at cap AND no demand).
			Assert.That(TransportEmploymentMath.EvaluatePurchase(2, 2, 0, 0, 4),
				Is.EqualTo(TransportPurchaseDecision.AtCap),
				"cap is checked before the demand gate");
		}

		[Test]
		public void TransportsFirstOutranksTheDemandGate()
		{
			// Under the cap, an idle transport available AND no demand — transports-first is reported.
			Assert.That(TransportEmploymentMath.EvaluatePurchase(1, 2, 1, 0, 4),
				Is.EqualTo(TransportPurchaseDecision.IdleTransportAvailable),
				"transports-first is checked before the demand gate");
		}

		[Test]
		public void ZeroCapMeansNoCeilingConfigured()
		{
			Assert.That(TransportEmploymentMath.EvaluatePurchase(5, 0, 0, 4, 4),
				Is.EqualTo(TransportPurchaseDecision.Buy),
				"cap <= 0 disables the ceiling rather than banning all purchases");
		}

		// ===== Mission-slot budget (the retargeted employment blocker) =====

		[Test]
		public void ReservedSliceIsDisabledAtZero()
		{
			Assert.That(TransportEmploymentMath.MissionSlotAvailable(0, 0), Is.False,
				"0 reserved slots = feature off, caller keeps its frozen shared-budget path");
			Assert.That(TransportEmploymentMath.MissionSlotAvailable(0, -1), Is.False);
		}

		[Test]
		public void ReservedSliceIsIndependentOfAttackSquadCount()
		{
			// The whole point: no attack-squad count appears in this signature, so three live attack
			// squads can no longer starve lift (HelicopterSquadBotModule.cs:892 asymmetry).
			Assert.That(TransportEmploymentMath.MissionSlotAvailable(0, 1), Is.True,
				"a free reserved slot is available regardless of how busy the attack loop is");
		}

		[Test]
		public void ReservedSliceFillsUp()
		{
			Assert.That(TransportEmploymentMath.MissionSlotAvailable(1, 1), Is.False,
				"the reserved slice is itself bounded");
			Assert.That(TransportEmploymentMath.MissionSlotAvailable(1, 2), Is.True);
			Assert.That(TransportEmploymentMath.MissionSlotAvailable(2, 2), Is.False);
		}

		// ===== Use-or-evac (behaviour c) =====

		[Test]
		public void IdleInsideTheWindowHolds()
		{
			Assert.That(TransportEmploymentMath.Decide(100, 900, false, true),
				Is.EqualTo(TransportDisposition.Hold),
				"still inside the patience window with no demand — keep it");
		}

		[Test]
		public void IdlePastTheWindowEvacuates()
		{
			Assert.That(TransportEmploymentMath.Decide(900, 900, false, true),
				Is.EqualTo(TransportDisposition.Evacuate),
				"the window boundary is inclusive");
			Assert.That(TransportEmploymentMath.Decide(2000, 900, false, true),
				Is.EqualTo(TransportDisposition.Evacuate),
				"well past the window still evacuates — terminal, no hold-and-recheck");
		}

		[Test]
		public void EvacIsDisabledAtZeroWindow()
		{
			Assert.That(TransportEmploymentMath.Decide(100000, 0, false, true),
				Is.EqualTo(TransportDisposition.Hold),
				"window <= 0 disables the evac branch entirely (frozen behaviour)");
		}

		[Test]
		public void EmploymentOutranksRetirement()
		{
			// Idle far past the window, but a lift is launchable RIGHT NOW.
			Assert.That(TransportEmploymentMath.Decide(5000, 900, true, true),
				Is.EqualTo(TransportDisposition.Employ),
				"a transport that can fly a lift this instant flies it rather than being refunded");
		}

		[Test]
		public void DemandWithNoFreeSlotDoesNotEmploy()
		{
			Assert.That(TransportEmploymentMath.Decide(10, 900, true, false),
				Is.EqualTo(TransportDisposition.Hold),
				"demand alone cannot launch without a mission slot");
		}

		[Test]
		public void DemandWithNoSlotStillEvacuatesPastTheWindow()
		{
			// Demand it can never serve (slice permanently full) must not pin the airframe forever.
			Assert.That(TransportEmploymentMath.Decide(900, 900, true, false),
				Is.EqualTo(TransportDisposition.Evacuate),
				"unservable demand past the window is still terminal");
		}

		[Test]
		public void NoDemandNoSlotHoldsWithinTheWindow()
		{
			// The DAMAGED-TRANSPORT path. The caller folds launchability into the demand argument
			// (HelicopterSquadBotModule.EvaluateIdleTransport), so a chip-damaged transport the launcher can
			// never pick arrives here with hasLiftDemand FALSE regardless of how many infantry are waiting.
			// Within the window it holds; past it, it must retire — see the next test. Before the blocker fix
			// this row was reached with demand TRUE, Employ shadowed Evacuate, and the airframe pinned forever.
			Assert.That(TransportEmploymentMath.Decide(100, 900, false, false),
				Is.EqualTo(TransportDisposition.Hold),
				"nothing to do and nowhere to do it — still inside the window");
		}

		[Test]
		public void NoDemandNoSlotEvacuatesPastTheWindow()
		{
			Assert.That(TransportEmploymentMath.Decide(900, 900, false, false),
				Is.EqualTo(TransportDisposition.Evacuate),
				"the damaged-transport path must terminate, or the wave reproduces River Zeta issue 4");
		}

		[Test]
		public void DemandAndSlotEmployWithinTheWindow()
		{
			Assert.That(TransportEmploymentMath.Decide(100, 900, true, true),
				Is.EqualTo(TransportDisposition.Employ),
				"a launchable transport with a waiting load flies immediately, not at the window");
		}

		[Test]
		public void DecideTableIsExhaustivelyPinned()
		{
			// Full demand x slot x window truth table. Employ iff (demand && slot) — and because the caller
			// folds IsReadyForMission into `demand`, Employ here means genuinely launchable. Otherwise the
			// window decides: Hold inside it, Evacuate at or past it.
			var within = 100;
			var past = 900;
			var window = 900;

			Assert.That(TransportEmploymentMath.Decide(within, window, true, true), Is.EqualTo(TransportDisposition.Employ));
			Assert.That(TransportEmploymentMath.Decide(within, window, true, false), Is.EqualTo(TransportDisposition.Hold));
			Assert.That(TransportEmploymentMath.Decide(within, window, false, true), Is.EqualTo(TransportDisposition.Hold));
			Assert.That(TransportEmploymentMath.Decide(within, window, false, false), Is.EqualTo(TransportDisposition.Hold));

			Assert.That(TransportEmploymentMath.Decide(past, window, true, true), Is.EqualTo(TransportDisposition.Employ));
			Assert.That(TransportEmploymentMath.Decide(past, window, true, false), Is.EqualTo(TransportDisposition.Evacuate));
			Assert.That(TransportEmploymentMath.Decide(past, window, false, true), Is.EqualTo(TransportDisposition.Evacuate));
			Assert.That(TransportEmploymentMath.Decide(past, window, false, false), Is.EqualTo(TransportDisposition.Evacuate));
		}

		// ===== Determinism =====

		[Test]
		public void DecisionsAreDeterministicAcrossRepeatedCalls()
		{
			var firstPurchase = TransportEmploymentMath.EvaluatePurchase(1, 2, 0, 4, 4);
			var firstDisposition = TransportEmploymentMath.Decide(901, 900, false, true);

			for (var i = 0; i < 64; i++)
			{
				Assert.That(TransportEmploymentMath.EvaluatePurchase(1, 2, 0, 4, 4), Is.EqualTo(firstPurchase),
					"purchase evaluation is a pure map from its arguments — zero RNG");
				Assert.That(TransportEmploymentMath.Decide(901, 900, false, true), Is.EqualTo(firstDisposition),
					"disposition is a pure map from its arguments — zero RNG");
			}
		}
	}
}
