#region Copyright & License Information
/*
 * WW3MOD supply-transfer arbitration — which way supply moves when a transport is ordered onto a
 * Logistics Centre, and how much moves when it does.
 *
 * THE GESTURE IS TWO-WAY AND THE POLARITY IS A USER RULING, not a derivation. A truck ordered onto
 * an LC is ambiguous on its face: both actors hold a pool and either could fill the other. The
 * ruling (2026-08-30) resolves it by INTENT rather than by arithmetic:
 *
 *     "the default action for trucks when ordered to an LC should be to resupply the LC, unless
 *      they are empty then they are themselves resupplied. If we use 'force-move' it could be
 *      inverted, so force move to a LC means it resupplies the truck."
 *
 * So: a loaded truck GIVES, an empty truck TAKES, and force-move always TAKES. The empty-truck
 * exception is not a special case bolted onto the default — it is the same rule stated once, that a
 * truck with nothing to give cannot be giving.
 *
 * WHY BOTH TARGETERS READ ONE FUNCTION. The two directions are offered by two separate
 * IOrderTargeters (DropsSupplyCache.DeliverSupplyOrderTargeter and .RestockOrderTargeter), and
 * UnitOrderGenerator.OrderForUnit returns the FIRST of them that matches walking down OrderPriority.
 * If each targeter carried its own copy of the polarity test, the two copies could come to disagree
 * and the priority order would silently decide which direction the player got — which is precisely
 * the bug fixed once already on this pair, where Restock matched under Ctrl too and made the
 * delivery order unreachable for every truck that had served anybody.
 *
 * Reading ONE function makes the directions disjoint BY CONSTRUCTION rather than by two predicates
 * that happen to agree today: exactly one of ToHost/ToTruck/None can be returned, so no ordering of
 * the two targeters can produce a different answer. The priority between them is therefore
 * uninteresting, and that is a property of this file rather than of the numbers over there.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Which way supply moves for a transport ordered onto a docking-aware host.</summary>
	public enum SupplyTransferDirection
	{
		/// <summary>Neither direction is available; the click is not ours to take.</summary>
		None,

		/// <summary>The transport fills the host — the loaded default. Shown with the wrench cursor.</summary>
		ToHost,

		/// <summary>The host fills the transport — force-move, or a transport with nothing to give.
		/// Shown with the enter cursor.</summary>
		ToTruck
	}

	public static class SupplyTransferMath
	{
		/// <summary>
		/// Resolves a click on a host into a transfer direction, or None when neither direction can do
		/// anything useful.
		/// </summary>
		/// <param name="forceMove">Player is holding the force-move modifier (Ctrl by default).</param>
		/// <param name="transportSupply">Supply currently aboard the transport.</param>
		/// <param name="transportCapacity">The transport's TotalSupply.</param>
		/// <param name="hostAbsorbs">The host can receive supply (carries AbsorbsSupplyCache).</param>
		/// <param name="hostDocks">The host can serve a docked transport (has a DockedCondition).</param>
		/// <remarks>
		/// THERE IS DELIBERATELY NO "is the transport damaged" TERM, and it was removed rather than never
		/// written. The predecessor of this method carried one, on the reasoning that a Centre repairs as
		/// well as refills so a full-but-damaged truck still has a reason to dock. That reasoning is sound
		/// and the term still did not work: the order it steers the click to is Restock, whose activity
		/// (<c>RestockSupply</c>) moves SUPPLY and nothing else. A full damaged truck was therefore sent
		/// on a drive that transferred zero and repaired nothing, under an enter cursor promising service.
		/// Returning None instead lets the click fall through to Repairable's own targeter at priority 5,
		/// which is the trait that actually repairs — so the repair gesture is reached by deleting a term,
		/// not by adding one.
		/// </remarks>
		public static SupplyTransferDirection ResolveDirection(
			bool forceMove, int transportSupply, int transportCapacity, bool hostAbsorbs, bool hostDocks)
		{
			// The two ways of asking to be served, stated as one condition because they are one rule:
			// force-move is the explicit request, and an empty transport is the implicit one.
			var wantsToBeServed = forceMove || transportSupply <= 0;

			if (wantsToBeServed)
			{
				// NO CURSOR OVER A NO-OP. A full transport cannot be served, so the click is refused
				// rather than previewed with an enter cursor that would do nothing on release. This is
				// the same rule the refused-attack work settled: the cursor and the order resolve through
				// one method, so a direction that cannot act must not claim the click.
				var canBeServed = transportSupply < transportCapacity;
				return hostDocks && canBeServed ? SupplyTransferDirection.ToTruck : SupplyTransferDirection.None;
			}

			// transportSupply > 0 is guaranteed here (otherwise wantsToBeServed was true), so the only
			// open question is whether this host can take a delivery at all.
			return hostAbsorbs ? SupplyTransferDirection.ToHost : SupplyTransferDirection.None;
		}

		/// <summary>
		/// How much supply a delivery moves from transport to host.
		///
		/// <para>THIS IS THE PARTIAL/PARTIAL POLICY, and it is deliberately the only place that decides
		/// it. The user's ruling covers a LOADED transport and an EMPTY one; it does not say what a
		/// half-full transport ordered onto a half-drained Centre should do, and that question has been
		/// put to them. Until it is answered the policy here is "give what fits" — the transport hands
		/// over as much as the host has headroom for and keeps any remainder, which is the reading that
		/// destroys no supply and leaves the player able to re-order either way.</para>
		///
		/// <para>When the answer arrives it changes THIS METHOD and nothing else: the direction
		/// arbitration above does not consult the amount, and the activity that performs the transfer
		/// asks only for a number.</para>
		/// </summary>
		public static int AmountToDeliver(int transportSupply, int hostSupply, int hostCapacity)
		{
			if (transportSupply <= 0)
				return 0;

			var headroom = hostCapacity - hostSupply;
			if (headroom <= 0)
				return 0;

			return transportSupply < headroom ? transportSupply : headroom;
		}
	}
}
