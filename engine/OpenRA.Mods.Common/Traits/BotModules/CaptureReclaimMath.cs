#region Copyright & License Information
/*
 * WW3MOD capture-reclaim decisions (pure math) — take our own cleared base back.
 *
 * PERCEIVED BEHAVIOUR: after a raid clears a bot's base, the neutralised structures do not sit there
 * forever. Since c513f358 a soldier entering ANY enemy building evicts the owner to Neutral and walks out,
 * so a cleared base is a row of Neutral buildings that only a technician can re-own. The capture layer could
 * not see them at all: PoiMap.Discover admits only the actor names in its IncomeWeights (oilb/fcom/bio/
 * miss/hosp) plus the Supply Route, so a neutralised afld/sam is not a POI and never enters
 * GetCaptureTargets. (NOT pbox/hbox/gtwr — those strip -CaptureManager/-Capturable outright in
 * structures-defenses.yaml, so they can be neither evicted nor reclaimed and are the wrong example to reach
 * for. AA defences inherit ^Defense → ^Building and ARE capturable.) This turns four coupled decisions into
 * integers so the coordinator stays a thin consumer and the logic is NUnit-pinned without a game run:
 *
 *   1. CombinedCaptureDemand — how much capturer demand to signal. The neutral-money-POI count the supply
 *      floor already scales to, PLUS the reclaim backlog, so a bot whose only remaining targets are its own
 *      cleared buildings still pulls technicians. Without this the floor reads zero demand once the map's
 *      free derricks are gone and recovery is unfundable.
 *   2. IsSafeToReclaim — the danger gate. A technician is a 250-cost consumable with no weapon; walking one
 *      into a base the raid has not left yet is a free kill. Compares BELIEVED anti-ground danger against a
 *      caller-supplied ceiling (both already in raw field units — the caller converts). NOTE this gate is a
 *      backstop, NOT the primary protection: its input is anti-correlated with the threat (see
 *      EscortSizingMath.AtLeast), which is why the caller also floors the reclaim escort at Light.
 *   3. ReclaimBudget — how many of the free capturers the reclaim pass may consume, so a preempting pass
 *      cannot starve the ranked pass to zero.
 *   4. UnmetReclaimDemand — backlog minus the candidates that actually have a body on them, the "we need
 *      more bodies" signal that lets the coordinator pull the floor on a scan where it DID dispatch someone.
 *
 * DETERMINISM (influence-stack invariant): ZERO random draws, pure integer comparisons over caller-supplied
 * values. No wall-clock, no collection enumeration. Two clients over the same synced state decide identically.
 *
 * OFF-SWITCH CONTRACT: CombinedCaptureDemand returns the money-POI count VERBATIM when reclaimEnabled is
 * false, and the coordinator does not call the other two at all when the lever is off — so a config that
 * omits ReclaimNeutralisedStructures is byte-identical to before this file existed. NUnit-pinned in
 * CaptureReclaimMathTest.
 *
 * v3-portable: engine-free static math; only the tasking plumbing that consumes it is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class CaptureReclaimMath
	{
		/// <summary>Total capturer demand to feed the supply floor: the reachable neutral MONEY POIs the floor
		/// already scales to, plus the reclaim backlog (our own structures sitting Neutral after an eviction).
		///
		/// <paramref name="reclaimEnabled"/> off ⇒ <paramref name="neutralMoneyPoiCount"/> verbatim, so a config
		/// that does not opt in reads exactly the number it read before. On ⇒ the two are summed: both are a
		/// "one capturer could be spent here" count, and a technician is CONSUMED by the capture it performs
		/// (ConsumedByCapture on ^CapturesNeutralBuildings), so backlog really is per-body demand rather than a
		/// pool that can be reused across targets.
		///
		/// The sum is the floor's INPUT, not the floor — the caller still clamps it to [TecnFloor, TecnFloorMax]
		/// via CaptureSupplyMath.EffectiveFloor, so a 12-building backlog cannot balloon the request pool.
		/// Pure integer, zero RNG.</summary>
		public static int CombinedCaptureDemand(int neutralMoneyPoiCount, int reclaimCandidateCount, bool reclaimEnabled)
		{
			if (!reclaimEnabled)
				return neutralMoneyPoiCount;

			return neutralMoneyPoiCount + reclaimCandidateCount;
		}

		/// <summary>May we send a capturer to a reclaim target sitting under this much BELIEVED anti-ground
		/// danger? Both arguments are RAW danger-field units (the caller converts its yaml "danger units" knob
		/// through DangerFieldLayer.GroundDangerUnitsToField), so this is a bare comparison and cannot disagree
		/// with the field's scale.
		///
		/// A NEGATIVE <paramref name="maxDangerField"/> disables the guard (always safe) — the escape hatch for
		/// a config that wants recovery attempted regardless. Zero means "outside every believed envelope",
		/// which converts losslessly since 0 units is 0 raw field units at any scale. The comparison is
		/// inclusive (at the ceiling is still safe) so a threshold set exactly at the ambient territory baseline
		/// does not refuse every target in our own back yard. Pure integer, zero RNG.</summary>
		public static bool IsSafeToReclaim(int groundDangerField, int maxDangerField)
		{
			if (maxDangerField < 0)
				return true;

			return groundDangerField <= maxDangerField;
		}

		/// <summary>How many of the free capturers the reclaim pass may consume this scan.
		///
		/// Reclaim runs BEFORE the ranked PoiMap pass and would otherwise drain the pool to empty, so a bot
		/// with three formerly-ours structures and one free derrick next door sends everybody to the former and
		/// nobody to the latter. Leaving exactly ONE capturer keeps reclaim's priority intact — it still gets
		/// every body but one — while guaranteeing the ranked pass is never starved to zero.
		///
		/// Two cases hand back the whole pool, both deliberate: <paramref name="rankedTargetCount"/> of 0 (the
		/// ranked pass has nothing to do, so reserving for it would just idle a capturer) and a single free
		/// capturer (reclaim is the priority; splitting one body is not possible and reclaim wins the tie).
		/// Pure integer, zero RNG.</summary>
		public static int ReclaimBudget(int freeCapturerCount, int rankedTargetCount)
		{
			if (freeCapturerCount <= 1 || rankedTargetCount <= 0)
				return freeCapturerCount;

			return freeCapturerCount - 1;
		}

		/// <summary>Reclaim targets that have no body on them — the shortfall that should pull production even
		/// though this scan DID dispatch somebody.
		///
		/// <paramref name="coveredCount"/> is candidates the caller has actually accounted for: dispatched this
		/// scan, PLUS those an in-flight capturer is already walking to. It is deliberately NOT "free capturers
		/// left over", which is the reading that makes this test fire unconditionally — dispatching 3 candidates
		/// with 3 capturers leaves 0 free and would report a shortfall of 3 when the true answer is 0.
		///
		/// Zero when every candidate is covered, and never negative, so the caller can treat it as a plain
		/// "> 0" gate. Pure integer, zero RNG.</summary>
		public static int UnmetReclaimDemand(int reclaimCandidateCount, int coveredCount)
		{
			var shortfall = reclaimCandidateCount - coveredCount;
			return shortfall > 0 ? shortfall : 0;
		}
	}
}
