#region Copyright & License Information
/*
 * WW3MOD @experimental — should a supply truck be sent to refill a Logistics Center? (pure decisions)
 *
 * USER RULING 2026-09-03: "Bots needs to learn how to resupply the LC (I think that should work now by
 * sending a truck to it, to transfer supplies to the LC from the truck. But make sure that works both for
 * bots and humans)."
 *
 * THE HEDGE IS RESOLVED, AND IN THE USER'S FAVOUR: truck -> Centre transfer ALREADY WORKS for a human, and
 * nothing in this file or its caller changes that path. Traced end to end at cb68ce61:
 *   * LOGISTICSCENTER carries AbsorbsSupplyCache (structures.yaml:560), which is the trait
 *     DropsSupplyCache.ResolveOrder:319 requires before it will accept a DeliverSupply order at all.
 *   * DropsSupplyCache.ResolveOrder:312-325 turns that order into QueueDriveAndDeliver.
 *   * Activities/DeliverSupply.cs:148-154 computes the amount through SupplyTransferMath.AmountToDeliver
 *     and then does `supply.DeductSupply(given)` followed by `hostProvider.AddSupply(given)` — a direct,
 *     atomic transfer into the Centre's own stock, not a crate drop.
 *   * DeliverSupply is the DEFAULT left-click of a loaded truck on a Centre; Ctrl+click is the mirror
 *     (RestockSupply, truck fills FROM the Centre). Both gestures are UI-complete.
 * So the human half needs no work. What was missing is that NO BOT MODULE EVER ISSUED THE ORDER — grep for
 * "DeliverSupply" across Traits/BotModules/ returns only prose. A bot's Centre therefore ran down to zero
 * and stayed there, which is the "units travel to an empty LC and then just sit there" report.
 *
 * ===== WHY THE DISPATCH LIVES WITH THE CENTRE AND NOT WITH THE TRUCK =====
 *
 * SupplyFollowerBotModule owns truck tasking, and the obvious home for "send a truck somewhere" is there.
 * It is the wrong home here, for a reason that is structural rather than stylistic: that module is a SHARED
 * instance (ai.yaml, SupplyFollowerBotModule@supply, RequiresCondition enable-ai-any), so @stable runs it,
 * while the Centre only exists for a profile that has a LogisticsCenterBotModule. Putting the decision next
 * to the thing it is about keeps the Centre's whole lifecycle — buy it, site it, deploy it, keep it stocked
 * — in one module, and keeps a behavioural change out of the benchmark control.
 *
 * THE ARBITRATION IS REAL AND IS NOT LEFT TO LUCK. Two modules ordering one truck is the order-spam failure
 * this codebase has hit repeatedly, so the dispatch takes a BotBlackboard claim ("logistics-delivery")
 * before it issues anything. SupplyFollowerBotModule filters its own roster on IsClaimedByOtherModule
 * (SupplyFollowerBotModule.cs:2576-2583, `claimant != null && claimant != "supply-follow"`), so a claimed
 * truck disappears from its scan entirely rather than being fought over. The claim is released the moment
 * the errand ends, which hands the truck straight back to the follower rather than stranding it — the
 * failure mode recorded in WORKSPACE/bugs/discovered.md for GarrisonBotModule, where a module dropped a
 * unit from its roster while keeping the claim and left it alive-and-claimed forever.
 *
 * DETERMINISM (influence-stack invariant): zero random draws, pure functions of caller-supplied scalars,
 * no world or actor references. Integer throughout. NUnit-pinned without a game run.
 */
#endregion

using System;

namespace OpenRA.Mods.Common.Traits
{
	public static class LogisticsCenterRestockMath
	{
		/// <summary><para>Is this Centre low enough to be worth a delivery run?</para>
		///
		/// <para>A FRACTION of capacity rather than an absolute, because the same Centre trait is used at two
		/// very different capacities (LOGISTICSCENTER 2250, a supply truck 750) and an absolute bar would
		/// mean something different at each. Per mille, matching how every other supply threshold in this
		/// codebase is expressed.</para>
		///
		/// <para>Note what is NOT here: "is it empty". Waiting for zero is waiting until the Centre has
		/// already failed the units standing at it — the report this fixes is units arriving at an empty
		/// Centre and sitting. The threshold exists so the truck is on its way BEFORE that.</para></summary>
		public static bool CentreNeedsRestock(int currentSupply, int totalSupply, int thresholdPerMille)
		{
			if (totalSupply <= 0 || thresholdPerMille <= 0)
				return false;

			if (currentSupply < 0)
				currentSupply = 0;

			// currentSupply * 1000 / totalSupply < threshold, without the division, so a small capacity
			// cannot round the answer sideways.
			return (long)currentSupply * 1000 < (long)thresholdPerMille * totalSupply;
		}

		/// <summary><para>How much this truck would actually move into that Centre. min(load, headroom).</para>
		///
		/// <para>SHAPED TO MATCH <see cref="SupplyTransferMath.AmountToDeliver"/> DELIBERATELY, and that file
		/// remains the authority: this is the DISPATCH-time prediction used to decide whether a drive is
		/// worth starting, and the activity recomputes the real figure on arrival against the stock as it is
		/// THEN. The two can legitimately disagree — the Centre may have been drawn down or topped up during
		/// the drive — and this one is never used to perform a transfer.</para></summary>
		public static int TransferableAmount(int truckSupply, int centreHeadroom)
		{
			if (truckSupply <= 0 || centreHeadroom <= 0)
				return 0;

			return truckSupply < centreHeadroom ? truckSupply : centreHeadroom;
		}

		/// <summary><para>Is this truck worth sending to that Centre at all?</para>
		///
		/// <para>Three refusals, and each is a real failure that has an analogue already recorded in this
		/// subsystem:</para>
		/// <list type="bullet">
		/// <item>A TRICKLE IS NOT WORTH A DRIVE. <paramref name="minDeliverySupply"/> stops the bot spending
		/// a truck's whole errand to move a handful of rounds, and — more importantly — stops a truck
		/// oscillating: deliver 20, drop below the follower's RestockThreshold, get released, get
		/// re-dispatched.</item>
		/// <item>A FULL CENTRE HAS NO HEADROOM, so the transfer would be zero and the drive wasted. This is
		/// the exact bug disclosed for the human gesture — a Centre deployed from an LCCV starts FULL at
		/// 2250/2250, so the first delivery ever attempted "drew a wrench, drove the whole way and
		/// transferred nothing".</item>
		/// <item>TOO FAR IS NOT WORTH IT. A truck hauled across the map is a truck not serving the army it
		/// was following, and it arrives long after the need.</item>
		/// </list></summary>
		public static bool WorthDispatching(
			int truckSupply, int centreHeadroom, int distanceCells, int minDeliverySupply, int maxDistanceCells)
		{
			if (distanceCells < 0 || (maxDistanceCells > 0 && distanceCells > maxDistanceCells))
				return false;

			return TransferableAmount(truckSupply, centreHeadroom) >= Math.Max(1, minDeliverySupply);
		}

		/// <summary><para>Rank two candidate trucks: LOWER is better. Distance first, load as the tie-break.</para>
		///
		/// <para>DISTANCE DOMINATES ON PURPOSE. The thing being bought here is a Centre that is stocked SOON,
		/// and the nearest adequate truck delivers soonest; picking the fullest truck instead routinely
		/// means picking one on the far side of the army. Load only separates trucks that are equally close,
		/// and it separates them the useful way — negated, so the fuller of two equidistant trucks wins and
		/// the Centre needs fewer round-trips.</para>
		///
		/// <para>Returns a long because distance is scaled by 100000 to make it strictly dominant: no
		/// realistic load can bridge one cell of distance, so the ordering cannot be inverted by a truck
		/// carrying a very large amount. The caller must still break exact ties by ActorID — two trucks with
		/// identical distance AND load are genuinely indistinguishable here, and world iteration order must
		/// not be allowed to decide (the determinism invariant).</para></summary>
		public static long DispatchRank(int distanceCells, int transferableAmount)
		{
			var d = Math.Max(0, distanceCells);
			var amount = Math.Max(0, transferableAmount);

			return (long)d * 100000L - amount;
		}

		/// <summary><para>Has a dispatched errand ended, so the claim must be handed back?</para>
		///
		/// <para>THE RELEASE IS THE HALF THAT GOES WRONG. A module that claims a unit and forgets to release
		/// it leaves that unit alive-and-claimed forever, invisible to every other claim-respecting module —
		/// the defect recorded against GarrisonBotModule and against this subsystem's own roster handling.
		/// So the ending conditions are enumerated rather than inferred from one flag:</para>
		/// <list type="bullet">
		/// <item><paramref name="truckGone"/> — dead, sold, or out of world.</item>
		/// <item><paramref name="centreGone"/> — destroyed, sold, or captured away from us mid-drive. The
		/// truck is still ours and still loaded, so releasing it hands it back to the follower rather than
		/// leaving it driving at a building we no longer own.</item>
		/// <item><paramref name="truckEmpty"/> — nothing left to give; the errand cannot succeed.</item>
		/// <item><paramref name="centreFull"/> — someone else topped it up, or it was never as low as we
		/// thought. Arriving would transfer zero.</item>
		/// <item><paramref name="truckIdle"/> — the activity finished or was cancelled. This is the ordinary
		/// SUCCESS path as well as the cancelled one, and the two are indistinguishable from here, which is
		/// why it releases rather than retries: a re-dispatch decision is made fresh next scan from the
		/// Centre's actual stock, which is the only thing that can tell them apart.</item>
		/// </list></summary>
		public static bool ErrandEnded(
			bool truckGone, bool centreGone, bool truckEmpty, bool centreFull, bool truckIdle)
		{
			return truckGone || centreGone || truckEmpty || centreFull || truckIdle;
		}
	}
}
