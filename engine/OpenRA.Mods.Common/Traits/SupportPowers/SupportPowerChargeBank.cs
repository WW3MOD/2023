#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// <para>The magazine behind a PURCHASED support power: how many shots the player has paid for and
	/// not yet fired. Split out of <see cref="SupportPowerInstance"/> because every question this
	/// feature turns on is answerable from three booleans and an int, and answering them here means
	/// they can be pinned by a unit test rather than only by launching the game
	/// (OpenRA.Test/SupportPowerChargeBankTest.cs).</para>
	///
	/// <para>THE WHOLE MODEL IS <see cref="Enabled"/> == false BY DEFAULT. A timer-charged power builds a
	/// bank with Enabled false, and every method below then degenerates to the identity: HidesIcon is
	/// always false, Consume always returns false and changes nothing, OverlayText is always null.
	/// That is deliberate and is the no-drift guarantee — a power that has not opted in cannot be
	/// altered by anything in this file, and the test file asserts exactly that rather than trusting it.</para>
	///
	/// <para>TWO SEPARATE QUESTIONS, AND CONFLATING THEM IS THE BUG THIS CLASS EXISTS TO PREVENT:
	///   "may this player have this power at all?"  -> `permitted`, passed IN from the instance
	///                                                 (lobby gate, faction prerequisite, WinState)
	///   "has this player paid for a shot?"         -> <see cref="Charges"/>, owned here
	/// The sidebar icon needs BOTH (<see cref="IconVisible"/>); the build menu needs ONLY the first
	/// (<see cref="CanPurchase"/>), because the entire point of the buy tab is to be usable while the
	/// bank is empty. Ask the wrong one and you get either a power that can never be bought or a
	/// host-disabled power sitting in the shop.</para>
	/// </summary>
	public sealed class SupportPowerChargeBank
	{
		/// <summary>True when this power is bought rather than charged on a timer.</summary>
		public readonly bool Enabled;

		/// <summary>Shots paid for and not yet fired.</summary>
		public int Charges { get; private set; }

		public SupportPowerChargeBank(bool enabled)
		{
			Enabled = enabled;
		}

		/// <summary>Bank a purchased shot. No-op for a timer-charged power.</summary>
		public void Grant(int count = 1)
		{
			if (!Enabled || count <= 0)
				return;

			Charges += count;
		}

		/// <summary>Spend a shot. Returns false (and changes nothing) if there was none to spend.</summary>
		public bool Consume()
		{
			if (!Enabled || Charges == 0)
				return false;

			Charges--;
			return true;
		}

		/// <summary>
		/// True when the power must be ABSENT from the support bin because nothing is banked.
		/// This is the "before they pop up in the side" half of the user's design; the power still
		/// exists and is still purchasable, it simply has no cameo yet.
		/// </summary>
		public bool HidesIcon => Enabled && Charges == 0;

		/// <summary>Does a cameo belong in the support bin? Needs permission AND stock.</summary>
		public bool IconVisible(bool permitted)
		{
			return permitted && !HidesIcon;
		}

		/// <summary>
		/// Does an entry belong in the build menu? Needs permission and NOTHING ELSE — deliberately
		/// independent of <see cref="Charges"/>, so a power can be restocked while one is banked.
		/// </summary>
		public bool CanPurchase(bool permitted)
		{
			return Enabled && permitted;
		}

		/// <summary>
		/// What to stamp across the cameo instead of "READY". Null at zero or one charge: at one, a
		/// purchased power is ready by definition and "READY" is the honest word; the count only
		/// starts carrying information once shots are stacked.
		/// </summary>
		public string OverlayText => Enabled && Charges > 1 ? "x" + Charges.ToStringInvariant() : null;
	}
}
