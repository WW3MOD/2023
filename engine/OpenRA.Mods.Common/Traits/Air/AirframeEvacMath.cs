#region Copyright & License Information
/*
 * WW3MOD airframe out-of-ammo disposition — rearm-or-evacuate decision (pure math).
 *
 * USER RULING (2026-08-19): "Airplanes uses the airfield, helicopters use helipad, if those do not exist they must
 * evacuate (They cannot be rearmed in that case)." This class is the decision half of that ruling; the ACTION half
 * is the pre-existing Evacuate disposition (RotateToEdge, which already flies an airframe past the map edge, grants
 * the `evacuating` condition and refunds GetEvacuationRefund).
 *
 * WHY A UNIT-SIDE LAYER EXISTS AT ALL. Every ground path for this question is closed to aircraft on purpose:
 * AmmoPool.AutoRearmIfAllEmpty returns immediately for anything with an AircraftInfo (AmmoPool.cs:233), so the
 * whole ResupplyBehavior axis — including its Evacuate case — never sees an airframe. Aircraft instead go through
 * Aircraft.OnBecomingIdle ⇒ ReturnToBase, whose no-resupplier branch queues FlyIdle and returns
 * (ReturnToBase.cs:126-128). That is why a spent helicopter on a mod with no helipad simply hovers: not a wedge,
 * just a permanent hold. The bot has covered its own helicopters since HelicopterSquadBotModule's EvacuateWhenIdle
 * (@experimental only); nothing covered a HUMAN player's.
 *
 * SCOPE OF THE TWO REFUSAL TERMS, stated honestly. Both shipped transports (TRAN Chinook, HALO Mi-8) inherit
 * ^Helicopter and carry NEITHER an AmmoPool NOR a Rearmable — they are Cargo airframes with no armament — so
 * today they are refused by the pool term and the Rearmable term alike, and no shipped actor separates the two.
 * The Rearmable term is kept anyway because it, not the pool count, is what the ruling actually says: an armed
 * transport (pools, no Rearmable) would read identically to a dry Apache on every other term, and the pool count
 * would wave it through. That case does not exist yet; the term costs one line and is pinned below.
 *
 * DETERMINISM (influence-stack invariant): zero RNG, pure integer/bool comparisons, no collection iteration.
 *
 * v3-portable: engine-free static math (NUnit-pinned in AirframeEvacMathTest); only the world-reading plumbing that
 * counts pools and asks AirframeReadiness for the host term is engine-specific.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Disposition for an airframe that may have run dry. <see cref="None"/> means "leave it alone".</summary>
	public enum AirframeEvacAction
	{
		/// <summary>Still armed, unarmed by design, hosted, or already leaving — no action.</summary>
		None,

		/// <summary>Spent with no rearm host: ammunition is one-way, so bank the salvage and leave the map.</summary>
		Evacuate,
	}

	public static class AirframeEvacMath
	{
		/// <summary>
		/// <para>The out-of-ammo disposition for an airframe.</para>
		///
		/// <para>The three refusals are ordered, and the order is the substance:</para>
		/// <list type="bullet">
		/// <item><paramref name="alreadyEvacuating"/> — re-issuing cancels the running RotateToEdge, so an
		/// undamped re-decision restarts the exit every tick and the airframe never reaches the edge.</item>
		/// <item><paramref name="designedToRearm"/> — the airframe carries a Rearmable naming a host. This is the
		/// term that states the ruling's actual scope: an airframe with no Rearmable makes the host term false
		/// PERMANENTLY (<see cref="ReturnToBase.AnyResupplierExists"/> returns false the moment RearmableInfo is
		/// null), so a rule phrased only as "no host ⇒ leave" retires airframes that were never meant to rearm
		/// and have therefore lost nothing by running dry.</item>
		/// <item><paramref name="totalPools"/> of zero — NOT "out of ammo", but unarmed by design.</item>
		/// <item><paramref name="hasRearmHost"/> — a host means ReturnToBase owns this airframe and will fly it to
		/// the pad. Evacuating a helicopter a captured helipad could have refilled throws it away.</item>
		/// </list>
		///
		/// <para>Only then is a spent, unhosted, orderable airframe evacuated. Terminal by design, matching
		/// <see cref="AmmoEvacMath"/>: no hold-and-recheck, because a parked disarmed airframe is the pathology
		/// this removes and a recheck loop turns one decision into an oscillation.</para>
		/// </summary>
		public static AirframeEvacAction Decide(int totalPools, int loadedPools, bool designedToRearm,
			bool hasRearmHost, bool alreadyEvacuating)
		{
			if (alreadyEvacuating)
				return AirframeEvacAction.None;

			// Never meant to rearm ⇒ not covered by the ruling. Must precede the host term — see remarks.
			if (!designedToRearm)
				return AirframeEvacAction.None;

			// Unarmed by design, not spent.
			if (totalPools <= 0)
				return AirframeEvacAction.None;

			// Any pool still holding rounds keeps the airframe in the fight. Mirrors
			// AirframeReadiness.AmmoReadyToFight's UNHOSTED reading (loaded > 0), which is the only honest one
			// here: a dry secondary on a loaded primary can still shoot, and where ammunition is one-way it will
			// never be able to say otherwise.
			if (loadedPools > 0)
				return AirframeEvacAction.None;

			if (hasRearmHost)
				return AirframeEvacAction.None;

			return AirframeEvacAction.Evacuate;
		}
	}
}
