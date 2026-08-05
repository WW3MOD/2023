#region Copyright & License Information
/*
 * WW3MOD @experimental — transport-helicopter employment math (pure integer).
 *
 * PERCEIVED BEHAVIOUR: transport helicopters stop being bought-and-forgotten. The frozen path buys a
 * transport on a flat lottery weight (ai.yaml `tran: 15` / `halo: 15`, limit 2, delay 2500) with NO demand
 * test, employs it only through a mission launcher that shares the attack squads' budget, and never retires
 * it — so a bought transport can sit at the Supply Route for the whole match (River Zeta issue 4).
 *
 * THREE BEHAVIOURS, one decision surface:
 *   a. DEMAND-GATED PURCHASE — a transport is called in only when there is real lift demand (infantry
 *      actually waiting for a ride). No demand, no purchase. This gate is mandatory.
 *   b. TRANSPORTS-FIRST — an already-owned IDLE transport outranks buying another. Demand is satisfied by
 *      employing what we have; only unmet demand justifies spending.
 *   c. USE-OR-EVAC — a transport idle beyond the patience window is TERMINAL: it evacuates to reserves
 *      (RotateToEdge banks the salvage refund, economy.md: GetSellValue x HP/MaxHP) and stops its upkeep
 *      drain. Deliberately no hold-and-recheck: a transport we could not employ for ~900 ticks is capital
 *      parked in a warzone, and the refund buys the thing we actually needed.
 *
 * WHY A SEPARATE MISSION BUDGET: TryLaunchTransportMission bails on `activeSquads.Count >= MaxActiveSquads`
 * but never ADDS to activeSquads — so three live attack squads starve transport missions permanently while
 * the counter never reflects a transport mission. That asymmetry (not a missing role case — the role filter
 * at TryLaunchTransportMission DOES select Role == Transport) is the real employment blocker. MissionSlot
 * gives lift its own reserved slice instead of making it lose every race to the attack loop.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, integer-only, no world/actor references —
 * plain scalars in, enum out. Every function is a pure deterministic map from its arguments, so ordering of
 * any caller-side collection cannot leak into a decision.
 *
 * GATING (NO LONGER byte-identical for every entry point). The three EMPLOYMENT flags below are still
 * @experimental-only and still default false/0: UnitBuilderBotModuleInfo.GateTransportOnDemand,
 * HelicopterSquadBotModuleInfo.TransportMissionSlots, HelicopterSquadBotModuleInfo.EvacuateIdleTransports —
 * EvaluatePurchase / ShouldBuy / MissionSlotAvailable / Decide are unreachable without one of them. But
 * InReserveZone and LoadCap are reached from the UNGATED lift path (HelicopterSquadBotModule.IsLiftCandidate /
 * TryLaunchTransportMission), so @stable DOES enter this file now — by deliberate user grant, since the raw
 * Actor.IsIdle passenger test it replaced meant no profile ever flew a lift at all. Still zero RNG, so the
 * lockstep-determinism half of the old argument is untouched; only the "@stable never gets here" half is.
 *
 * Split out as a pure static class (mirrors ForceCompositionMath / TransportLoadMath / HeliEmploymentMath)
 * so the whole decision is NUnit-pinned WITHOUT a game run.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Why a transport call-in was or was not authorised. Non-Buy values are diagnostic: they name
	/// the binding constraint so a bot-debug line can explain an absent purchase.</summary>
	public enum TransportPurchaseDecision
	{
		/// <summary>Unmet lift demand and headroom under the cap — call one in.</summary>
		Buy,

		/// <summary>Owned count already at the configured ceiling.</summary>
		AtCap,

		/// <summary>An idle transport we already own can serve this demand (transports-first).</summary>
		IdleTransportAvailable,

		/// <summary>Nobody is waiting for a lift — the mandatory demand gate.</summary>
		NoDemand,
	}

	/// <summary>What to do with a transport that is not currently carrying or loading cargo.</summary>
	public enum TransportDisposition
	{
		/// <summary>Keep it in the pool — no demand yet, still inside the patience window.</summary>
		Hold,

		/// <summary>Launch a lift now: demand exists and a mission slot is free.</summary>
		Employ,

		/// <summary>Terminal — idle past the window with nothing to do. Evac to reserves for the refund.</summary>
		Evacuate,
	}

	public static class TransportEmploymentMath
	{
		/// <summary>Is there real lift demand? <paramref name="liftCandidates"/> is the count of infantry the
		/// caller has established are actually available AND willing to ride (idle, cargo-type compatible, not
		/// already committed elsewhere); <paramref name="minPassengers"/> is the load threshold a mission needs
		/// (TransportMinInfantry). Demand exists only when a FULL minimum load could be assembled — a single
		/// stray rifleman is not a reason to commit a 2000-credit airframe.</summary>
		public static bool HasLiftDemand(int liftCandidates, int minPassengers)
		{
			if (minPassengers < 1)
				minPassengers = 1;

			return liftCandidates >= minPassengers;
		}

		/// <summary>Authorise (or refuse) calling in one more transport helicopter.
		///   <paramref name="ownedTransports"/>   — transports already owned (alive, in world), incl. busy ones.
		///   <paramref name="maxTransports"/>     — ceiling; 0 or less means "no ceiling configured".
		///   <paramref name="idleTransports"/>    — owned transports currently employable (idle, mission-ready).
		///   <paramref name="liftCandidates"/>    — infantry available and waiting for a ride.
		///   <paramref name="minPassengers"/>     — passengers a mission needs to launch.
		/// Order is deliberate and is the whole policy: cap first (a hard ceiling can never be spent past),
		/// then TRANSPORTS-FIRST (an idle airframe we already paid for must be employed before buying another
		/// — behaviour b), then the mandatory DEMAND gate (behaviour a). Only unmet demand reaches Buy.</summary>
		public static TransportPurchaseDecision EvaluatePurchase(int ownedTransports, int maxTransports,
			int idleTransports, int liftCandidates, int minPassengers)
		{
			if (maxTransports > 0 && ownedTransports >= maxTransports)
				return TransportPurchaseDecision.AtCap;

			// Transports-first: capacity we already own satisfies the demand, so buying now would just add a
			// second idle airframe. Checked BEFORE the demand gate so the reported reason is the useful one.
			if (idleTransports > 0)
				return TransportPurchaseDecision.IdleTransportAvailable;

			if (!HasLiftDemand(liftCandidates, minPassengers))
				return TransportPurchaseDecision.NoDemand;

			return TransportPurchaseDecision.Buy;
		}

		/// <summary>Convenience predicate for the purchase gate — true only on <see cref="TransportPurchaseDecision.Buy"/>.</summary>
		public static bool ShouldBuy(int ownedTransports, int maxTransports, int idleTransports,
			int liftCandidates, int minPassengers)
		{
			return EvaluatePurchase(ownedTransports, maxTransports, idleTransports, liftCandidates, minPassengers)
				== TransportPurchaseDecision.Buy;
		}

		/// <summary>How many passengers one lift may ORDER aboard.
		///   <paramref name="maxInfantry"/>    — the doctrine cap (a squad-sized load); 0 or less = no own cap.
		///   <paramref name="cargoMaxWeight"/> — the airframe's physical capacity.
		///   <paramref name="minInfantry"/>    — the launch threshold; the result is never below it.
		/// The airframe capacity ALONE is the wrong cap and is actively harmful. Ordering `EnterTransport` is
		/// not a request — Passenger.ResolveOrder queues RideTransport non-queued (Passenger.cs:207), cancelling
		/// whatever each soldier was doing, and reserving cargo space LOCKS the transport (Cargo.LockForPickup,
		/// Cargo.cs:333-349). But the load dispatches as soon as `minInfantry` are aboard
		/// (TransportLoadMath.Decide), so every extra ordered soldier is left chasing a departing helicopter
		/// while still holding its reservation — and Cargo.ReleaseLock only fires at reservedWeight == 0
		/// (Cargo.cs:351-353), so the stragglers pin the airframe's lock. The America `tran` carries
		/// MaxWeight 36 against a launch threshold of 4, so the uncapped form ordered 36 soldiers for a 4-man
		/// lift. Mirrors MountedTransportBotModule's Math.Min(MaxPassengersPerLoad, MaxWeight).</summary>
		public static int LoadCap(int maxInfantry, int cargoMaxWeight, int minInfantry)
		{
			var cap = cargoMaxWeight;
			if (maxInfantry > 0 && maxInfantry < cap)
				cap = maxInfantry;

			// Never cap below the launch threshold: a cap under minInfantry could never assemble a load at all.
			if (cap < minInfantry)
				cap = minInfantry;

			return cap < 1 ? 1 : cap;
		}

		/// <summary>Is a soldier close enough to home to count as LIFTABLE RESERVE?
		///   <paramref name="distanceSqCells"/> — squared cell distance from the soldier to our own Supply Route.
		///   <paramref name="radiusCells"/>     — reserve-zone radius; 0 or less means "no spatial restriction".
		/// This is the gate that makes dropping the old raw-IsIdle passenger test safe. Availability for a lift
		/// is not idleness (infantry on the line engage through AutoTarget and are never idle, so the idle pool
		/// is empty in practice and no lift ever launched) — it is being part of the REAR RESERVE. Troops past
		/// the radius are already committed to the line and keep their forward orders. Mirrors
		/// MountedTransportBotModule's ReserveZoneRadiusCells bubble.</summary>
		public static bool InReserveZone(long distanceSqCells, int radiusCells)
		{
			if (radiusCells <= 0)
				return true;

			return distanceSqCells <= (long)radiusCells * radiusCells;
		}

		/// <summary>Is a transport-mission slot free? Lift gets its OWN reserved budget rather than competing
		/// with the attack loop for MaxActiveSquads (which transport missions never increment — the starvation
		/// asymmetry). <paramref name="maxTransportMissions"/> of 0 or less means the reserved slice is
		/// disabled, in which case the caller keeps its frozen shared-budget behaviour.</summary>
		public static bool MissionSlotAvailable(int activeTransportMissions, int maxTransportMissions)
		{
			if (maxTransportMissions <= 0)
				return false;

			return activeTransportMissions < maxTransportMissions;
		}

		/// <summary>Decide what to do with an idle transport.
		///   <paramref name="idleTicks"/>          — consecutive ticks it has been idle and unemployed.
		///   <paramref name="evacuateIdleTicks"/>  — patience window; 0 or less disables the evac branch.
		///   <paramref name="hasLiftDemand"/>      — infantry are waiting (see <see cref="HasLiftDemand"/>).
		///   <paramref name="missionSlotFree"/>    — a transport-mission slot is available right now.
		/// Employment outranks retirement: a transport that CAN fly a lift this instant always flies it, even
		/// if it has been idle past the window — the window only decides the fate of a transport that has had
		/// no work. The evac is terminal (no hold-and-recheck): once the window elapses with no employable
		/// demand, the airframe is refunded rather than re-examined next tick.</summary>
		public static TransportDisposition Decide(int idleTicks, int evacuateIdleTicks,
			bool hasLiftDemand, bool missionSlotFree)
		{
			if (hasLiftDemand && missionSlotFree)
				return TransportDisposition.Employ;

			if (evacuateIdleTicks > 0 && idleTicks >= evacuateIdleTicks)
				return TransportDisposition.Evacuate;

			return TransportDisposition.Hold;
		}
	}
}
