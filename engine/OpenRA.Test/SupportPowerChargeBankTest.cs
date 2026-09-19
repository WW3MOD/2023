#region Copyright & License Information
/*
 * WW3MOD SupportPowerChargeBank tests — the state machine behind BUYING a support power instead of
 * waiting for it (user, 2026-09-06: powers get their own build tab, "and at that point they are
 * already fully loaded").
 *
 * WHY THIS FILE EXISTS. Two questions look like one and are not, and the whole feature is the
 * difference between them:
 *
 *     "may this player have this power at all?"   -> permitted (lobby tick, faction, WinState)
 *     "has this player paid for a shot?"          -> charges
 *
 * The SIDEBAR CAMEO needs both. The BUILD MENU needs only the first — and that is the one an
 * implementation gets wrong, because the intuitive reading of "the power is not available" is that
 * it should also not be for sale. Conflate them and the shop is empty exactly when the player wants
 * to use it: nothing is ever buyable, because nothing is ever banked, because nothing is ever
 * buyable. Both directions are pinned below.
 *
 * SCOPE, HONESTLY. This covers the bank's arithmetic and the two gate questions it answers. It does
 * NOT cover: that SupportPowerInstance really wires `permitted` to the lobby condition (verified by
 * reading — RequiresCondition -> IsTraitDisabled -> instancesEnabled -> Permitted), that the
 * production queue really calls GrantCharge on completion, or anything about the tab rendering.
 * Those need a World and a widget. Said plainly so a green run here is not mistaken for whole-
 * feature cover.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupportPowerChargeBankTest
	{
		static SupportPowerChargeBank Purchased(int charges = 0)
		{
			var bank = new SupportPowerChargeBank(true);
			bank.Grant(charges);
			return bank;
		}

		// ---------- the no-drift guarantee ----------
		// A power that has not opted in must be untouched by every method here. This is the reason
		// RequiresPurchase defaults false rather than being inferred from anything.

		[Test]
		public void TimerChargedPowerIsInertInEveryDirection()
		{
			var bank = new SupportPowerChargeBank(false);

			bank.Grant(5);
			Assert.That(bank.Charges, Is.Zero, "a timer power must not accumulate a bank");
			Assert.That(bank.Consume(), Is.False, "a timer power must not consume shots");
			Assert.That(bank.HidesIcon, Is.False, "a timer power's cameo must never be hidden by stock");
			Assert.That(bank.OverlayText, Is.Null, "a timer power must not stamp a count on its cameo");

			// The load-bearing one: Disabled is `!IconVisible(Permitted)`, so this identity is what
			// makes the new expression byte-identical to the old `Disabled => !Permitted`.
			Assert.That(bank.IconVisible(true), Is.True);
			Assert.That(bank.IconVisible(false), Is.False);
		}

		[Test]
		public void TimerChargedPowerIsNeverForSale()
		{
			var bank = new SupportPowerChargeBank(false);

			Assert.That(bank.CanPurchase(true), Is.False,
				"a power with no proxy actor and a live ChargeInterval must not appear in the shop");
		}

		// ---------- "before they pop up in the side" ----------

		[Test]
		public void UnboughtPowerIsAbsentFromTheBinButPresentInTheShop()
		{
			var bank = Purchased();

			Assert.That(bank.HidesIcon, Is.True);
			Assert.That(bank.IconVisible(true), Is.False, "an empty magazine must show no cameo");
			Assert.That(bank.CanPurchase(true), Is.True, "an empty magazine is exactly when it must be buyable");
		}

		[Test]
		public void PurchaseMakesTheCameoAppear()
		{
			var bank = Purchased();
			bank.Grant();

			Assert.That(bank.Charges, Is.EqualTo(1));
			Assert.That(bank.HidesIcon, Is.False);
			Assert.That(bank.IconVisible(true), Is.True);

			// Null, not "x1": at one shot the widget falls through to its own READY text. A
			// purchased power in the bin is ready by definition, so READY is the honest word and a
			// count would be noise.
			Assert.That(bank.OverlayText, Is.Null);
		}

		// ---------- the gate the host controls ----------

		[Test]
		public void HostDisabledPowerCannotBeBought()
		{
			var bank = Purchased();

			Assert.That(bank.CanPurchase(false), Is.False,
				"a power the host switched off must have no entry in the build tab at all");
		}

		[Test]
		public void HostDisabledPowerShowsNoCameoEvenWithShotsBanked()
		{
			// The order matters: bought while allowed, then disallowed. Permission is re-read every
			// frame, so revoking it must hide the cameo without needing the bank cleared.
			var bank = Purchased(2);

			Assert.That(bank.IconVisible(true), Is.True);
			Assert.That(bank.IconVisible(false), Is.False);
		}

		/// <summary>
		/// THE CLAIM THE WHOLE DEFCON BAND DESIGN RESTS ON, previously true only by reading. Revoking a
		/// condition on a weapon the player has ALREADY PAID FOR must cost them nothing: a band that
		/// locks and unlocks is a cameo appearing and disappearing over an untouched magazine. If this
		/// were false, every level change in an Escalation match would quietly confiscate stock, and the
		/// ruling that a lock is free would have to be withdrawn rather than merely re-tested.
		///
		/// WHAT MAKES THE LOOP MEANINGFUL RATHER THAN DECORATIVE: permission is not stored here at all.
		/// It is an argument, recomputed by SupportPowerInstance on every tick and passed in, so a cycle
		/// is exactly this pair of calls and there is no revoke path with a side effect to miss.
		/// The same holds one level up, verified by reading rather than by this test, which cannot build
		/// a World: the only bank mutators the instance owns are GrantCharge (purchase completed) and the
		/// single Consume on the FIRE path (SupportPowerManager.cs:279, :332, :480). `Permitted` reaches
		/// the bank through Disabled and Purchasable only, and both are read-only projections; Tick and
		/// PrerequisitesAvailable reset remainingSubTicks and nothing else.
		/// </summary>
		[Test]
		public void ABankedShotSurvivesAnyNumberOfLockAndUnlockCycles()
		{
			var bank = Purchased(1);

			for (var i = 0; i < 100; i++)
			{
				Assert.That(bank.IconVisible(false), Is.False, $"cycle {i}: a locked band must take the cameo down");
				Assert.That(bank.IconVisible(true), Is.True, $"cycle {i}: and unlocking must bring it straight back");
				Assert.That(bank.Charges, Is.EqualTo(1), $"cycle {i}: the cycle spent a shot the player had paid for");
			}

			// AND IT IS STILL SPENDABLE. A bank that survived the cycles as a number but refused to fire
			// afterwards would satisfy the loop above and still lose the player their purchase.
			Assert.That(bank.Consume(), Is.True, "the surviving shot could not be fired");
			Assert.That(bank.Charges, Is.Zero);
		}

		[Test]
		public void LockingAnEmptyBankNeitherBanksNorSpendsAnything()
		{
			// The other direction, and the one an off-by-one would break: cycling while the magazine is
			// empty must not underflow it, and must not hand out a free shot on the way back up either.
			var bank = Purchased();

			for (var i = 0; i < 100; i++)
			{
				Assert.That(bank.IconVisible(false), Is.False);
				Assert.That(bank.IconVisible(true), Is.False,
					$"cycle {i}: an empty magazine must stay out of the bin however often the band cycles");
			}

			Assert.That(bank.Charges, Is.Zero);
			Assert.That(bank.HidesIcon, Is.True);

			// The shop stays open throughout -- the half of the model a locked band must not close, since
			// CanPurchase is the host gate and nothing else.
			Assert.That(bank.CanPurchase(true), Is.True);
		}

		// ---------- one purchase, one shot ----------

		[Test]
		public void FiringConsumesExactlyOneShot()
		{
			var bank = Purchased(1);

			Assert.That(bank.Consume(), Is.True);
			Assert.That(bank.Charges, Is.Zero);
			Assert.That(bank.HidesIcon, Is.True, "the cameo must leave the bin once the last shot is fired");
		}

		[Test]
		public void FiringAnEmptyBankIsARefusalRatherThanAnUnderflow()
		{
			var bank = Purchased();

			Assert.That(bank.Consume(), Is.False);
			Assert.That(bank.Charges, Is.Zero, "must not go negative");
		}

		[Test]
		public void StackedPurchasesSurviveOneFiring()
		{
			var bank = Purchased();
			bank.Grant();
			bank.Grant();

			Assert.That(bank.Charges, Is.EqualTo(2));
			Assert.That(bank.OverlayText, Is.EqualTo("x2"));

			bank.Consume();

			Assert.That(bank.Charges, Is.EqualTo(1));
			Assert.That(bank.IconVisible(true), Is.True, "a stacked bank must keep the cameo up after one shot");
			Assert.That(bank.OverlayText, Is.Null, "and must drop back to READY rather than reading x1");
		}

		[Test]
		public void GrantIgnoresNonPositiveCounts()
		{
			var bank = Purchased(3);

			bank.Grant(0);
			bank.Grant(-2);

			Assert.That(bank.Charges, Is.EqualTo(3));
		}
	}
}
