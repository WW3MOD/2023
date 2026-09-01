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
 * "EMPTY" WAS THEN GIVEN A NUMBER by a follow-up ruling the same day: at or below the transport's own
 * RestockThreshold (50 on TRUK), not literally zero. Same rule, made actionable — a truck holding 20
 * has nothing worth giving, and letting it deliver produced a dribble-into-the-depot-then-immediately-
 * refill loop. The number is READ OFF THE TRANSPORT rather than restated here, so the mod carries one
 * tuned constant for this and not two.
 *
 * WHY BOTH TARGETERS READ ONE FUNCTION. The two directions are offered by two separate
 * IOrderTargeters (DropsSupplyCache.DeliverSupplyOrderTargeter and .RestockOrderTargeter), and
 * UnitOrderGenerator.OrderForUnit returns the FIRST of them that matches walking down OrderPriority.
 * If each targeter carried its own copy of the polarity test, the two copies could come to disagree
 * and the priority order would silently decide which direction the player got — which is precisely
 * the bug fixed once already on this pair, where Restock matched under Ctrl too and made the
 * delivery order unreachable for every truck that had served anybody.
 *
 * Be precise about what that buys, because the first account of it overclaimed. A single-valued enum
 * return cannot answer twice, so "the two directions are disjoint" is not a property anything could
 * violate, nor one a test could fail — it is a restatement of the return type. The REAL guarantee is
 * narrower and still worth having: both targeters read the same inputs through the same branch, so
 * they cannot drift apart the way two hand-maintained predicates did, and the 6/7 priority between
 * them therefore never decides anything.
 *
 * The property that IS worth testing is a different one — that every direction returned can actually
 * MOVE supply on arrival (NoInputEverYieldsADirectionThatCannotMoveSupply). A direction that cannot
 * act is not a harmless no-op: it draws a cursor promising something that will not happen, and at
 * priority 6/7 it silently vetoes Repairable at 5, which is the trait that would have acted.
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
		/// <param name="transportRestockThreshold">
		/// The level at or below which the transport counts as having nothing to give, and so receives
		/// instead. This is the transport's OWN <c>SupplyProvider.RestockThreshold</c> — the number it
		/// already uses to decide when to go and refill itself — passed in rather than restated. USER
		/// RULING 2026-08-30: "a truck at or under 50 supply receives from the Centre; anything above 50
		/// gives to it", chosen to reuse the tuned number already shipping on the truck instead of
		/// introducing a second constant, and to stop a nearly-dry transport dribbling 20 supply into a
		/// depot and immediately needing to go refill. A threshold of 0 restores the literal
		/// empty-means-zero reading.
		/// </param>
		/// <param name="hostSupply">Supply currently held by the host.</param>
		/// <param name="hostCapacity">The host's TotalSupply.</param>
		/// <param name="hostAbsorbs">The host can receive supply (carries AbsorbsSupplyCache).</param>
		/// <param name="hostDocks">The host can serve a docked transport (has a DockedCondition).</param>
		/// <remarks>
		/// <para>THE HOST'S POOL IS A REQUIRED PARAMETER, not an optional refinement, and it is required
		/// because it was once absent. The first cut of this method took only the transport's supply and
		/// capacity, so it answered ToHost for any loaded transport and any absorbing host — including a
		/// host with no room. A Logistics Centre STARTS FULL (<c>SupplyProvider</c> initialises
		/// <c>currentSupply</c> from <c>TotalSupply</c>), so the very first thing a player does with this
		/// gesture — send a loaded truck to a freshly deployed Centre — drew the wrench cursor, drove the
		/// truck the whole way, and transferred nothing. Making the pool a parameter with no default is
		/// what stops a future call site quietly reintroducing that.</para>
		///
		/// <para>THERE IS DELIBERATELY NO "is the transport damaged" TERM. The predecessor carried one,
		/// reasoning that a Centre repairs as well as refills so a full-but-damaged transport still has a
		/// reason to dock. It is worth being precise about what deleting it did and did not do, because
		/// the first account of this was wrong. Under the OLD polarity the term was DEAD on a Centre:
		/// Restock was gated on <c>!ForceMove</c>, so force-move on a full transport went to Deliver and
		/// never reached it. Deleting it therefore changed nothing observable against the code that
		/// shipped. What it does do is keep the term from becoming live under the NEW polarity, where it
		/// would have sent a full damaged transport to Restock — an order whose activity moves supply and
		/// nothing else, so the drive would transfer zero and repair nothing while an enter cursor
		/// promised service. Returning None instead lets the click fall to Repairable's own targeter at
		/// priority 5. The honest scope of that: repair-by-click is reachable only by force-move on an
		/// EXACTLY full transport. A damaged transport holding any supply at all still cannot be sent to
		/// a Centre for repair by any click, before this change or after it.</para>
		/// </remarks>
		public static SupplyTransferDirection ResolveDirection(
			bool forceMove, int transportSupply, int transportCapacity, int transportRestockThreshold,
			int hostSupply, int hostCapacity, bool hostAbsorbs, bool hostDocks)
		{
			// The two ways of asking to be served, stated as one condition because they are one rule:
			// force-move is the explicit request, and a transport with nothing worth giving is the
			// implicit one. "Nothing worth giving" is the threshold, not zero — see the parameter.
			var wantsToBeServed = forceMove || transportSupply <= transportRestockThreshold;

			if (wantsToBeServed)
			{
				// NO CURSOR OVER A NO-OP, and it takes BOTH pools to know. The transport must have room
				// to receive and the host must have something to give; either one missing makes the
				// order a drive that transfers nothing. This is the rule the refused-attack work settled:
				// the cursor and the order resolve through one method, so a direction that cannot act
				// must not claim the click. (DropsSupplyCache.NearestRestockHost has always applied the
				// hostSupply half when picking a host automatically; the targeter never did.)
				var canBeServed = transportSupply < transportCapacity && hostSupply > 0;
				return hostDocks && canBeServed ? SupplyTransferDirection.ToTruck : SupplyTransferDirection.None;
			}

			// transportSupply > transportRestockThreshold is guaranteed here (otherwise wantsToBeServed
			// was true), so for any threshold >= 0 there is something aboard to give. What is left to
			// establish is that this host can take a delivery AND has somewhere to put it.
			return hostAbsorbs && hostSupply < hostCapacity
				? SupplyTransferDirection.ToHost
				: SupplyTransferDirection.None;
		}

		/// <summary>
		/// How far from a host's CENTRE cell a transport may stop and still count as having arrived.
		///
		/// <para><b>DO NOT SIMPLIFY THIS TO A CONSTANT.</b> It looks like defensive padding and it is
		/// not: the footprint term is what makes the guard usable at all, and dropping it breaks the
		/// feature in the direction that is hardest to diagnose — every legitimate delivery silently
		/// refusing, with the truck parked at the Centre and the supply still aboard.</para>
		///
		/// <para>The arithmetic, so it does not have to be re-derived. The drive is aimed at the cell
		/// containing the host's CenterPosition, and <c>ArrivedAtDropCell</c> tests
		/// <c>dx² + dy² &lt;= tolerance²</c> against that cell. A 3x3 Centre occupies a ring one cell
		/// thick around it, so a transport that stops legitimately alongside is already at dx=1..2 —
		/// and on the diagonal approach at dx=2, dy=2, i.e. 4 + 4 = 8. A flat margin of 2 gives 4, so
		/// it REJECTS the ordinary corner approach. Adding the footprint radius (3/2 = 1) gives 3,
		/// hence 9, which admits the corner and still rejects anything that never left: a transport
		/// refused a path completes its Move where it stands, which on this map is 20 cells away and
		/// nowhere near 9.</para>
		///
		/// <para>The margin and the radius are therefore doing DIFFERENT jobs — the radius covers the
		/// host's own body, the margin covers how far outside it a transport may park — and collapsing
		/// them into one number couples the guard to one building size.</para>
		/// </summary>
		/// <param name="hostFootprintCells">The larger of the host's two footprint dimensions, or 0.</param>
		/// <param name="approachMarginCells">How far outside the footprint a transport may stop.</param>
		public static int ArrivalTolerance(int hostFootprintCells, int approachMarginCells)
		{
			var radius = hostFootprintCells > 0 ? hostFootprintCells / 2 : 0;
			return radius + approachMarginCells;
		}

		/// <summary>
		/// The approach margin used when a caller has no tuned one to hand. Referenced by
		/// <c>DropsSupplyCacheInfo.DropAtToleranceCells</c> rather than restated there, so the mod carries
		/// one number for this and not two — the same discipline the RestockThreshold parameter above is
		/// documented under.
		/// </summary>
		public const int DefaultApproachMarginCells = 2;

		/// <summary>
		/// Has a transport on a supply errand actually reached its host?
		///
		/// <para>THE ONE COMPOSITION OF footprint -> tolerance -> distance, and it exists as a named method
		/// because having the three steps written out at each errand is what let one of them ship without
		/// the check at all. <see cref="DeliverSupply"/> carried the guard from the start;
		/// <see cref="RestockSupply"/>, its documented mirror, never had it, and the asymmetry survived
		/// review because the arithmetic was open-coded at the one site that had it rather than named.</para>
		///
		/// <para>Why the guard is load-bearing rather than defensive: a Move to a cell with no route does
		/// not FAIL. <c>PathFinder</c> bails to NoPath, and <c>Move.Tick</c> treats an empty path as arrival
		/// (Move.cs:173-177) — it sets destination to the current cell and completes in about two ticks. So
		/// an errand that transfers supply on completion transfers it from wherever the transport happened
		/// to be standing when the order was given.</para>
		/// </summary>
		public static bool ArrivedAtHost(int dx, int dy, int hostFootprintCells, int approachMarginCells)
		{
			return SupplyDropMath.ArrivedAtDropCell(
				dx, dy, ArrivalTolerance(hostFootprintCells, approachMarginCells));
		}

		/// <summary>
		/// How much supply a restock moves from host to transport — the mirror of
		/// <see cref="AmountToDeliver"/>, and deliberately the same shape so the two directions of one
		/// gesture cannot drift apart.
		///
		/// <para>THE ARRIVAL TERM IS A PARAMETER RATHER THAN THE CALLER'S BUSINESS, and that is the whole
		/// point of routing the amount through here: an activity cannot compute a transfer without having
		/// been made to answer whether it arrived. Passing <c>false</c> yields zero, so the failure mode is
		/// "the truck keeps its load", which is always recoverable.</para>
		/// </summary>
		public static int AmountToRestock(bool arrived, int transportSupply, int transportCapacity, int hostSupply)
		{
			var needed = transportCapacity - transportSupply;
			if (needed <= 0 || hostSupply <= 0)
				return 0;

			return needed < hostSupply ? needed : hostSupply;
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
