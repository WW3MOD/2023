#region Copyright & License Information
/*
 * WW3MOD — evacuate an airframe whose ammunition has become one-way.
 *
 * USER RULING (2026-08-19): "Airplanes uses the airfield, helicopters use helipad, if those do not exist they must
 * evacuate (They cannot be rearmed in that case)."
 *
 * This is the world-reading plumbing only; the judgement is AirframeEvacMath.Decide and the action is the
 * pre-existing RotateToEdge evac (past the map edge, `evacuating` condition, GetEvacuationRefund).
 */
#endregion

using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Evacuate this airframe off the map (refunding its evacuation value) once every ammo pool is empty and",
		"no rearm host it could use exists in the world. Implements the 2026-08-19 ruling that helicopters rearm",
		"at a helipad and otherwise leave, since a mod with no helipad on the map makes their ammunition one-way.",
		"Inert on any airframe without a Rearmable (the transports), so it is safe to attach to a whole template.")]
	public class EvacuateWhenUnrearmableInfo : ConditionalTraitInfo, Requires<AircraftInfo>
	{
		[Desc("Also evacuate airframes owned by a bot. OFF by default: bot airframe dispositions are owned by",
			"HelicopterSquadBotModule (EvacuateWhenIdle), which keeps its own `evacuating` bookkeeping so a heli",
			"flying its exit is never re-adopted or re-tasked. A second, module-blind evacuator would issue",
			"RotateToEdge behind the module's back and the next squad order would cancel it mid-flight.")]
		public readonly bool IncludeBotOwners = false;

		public override object Create(ActorInitializer init) { return new EvacuateWhenUnrearmable(this); }
	}

	public class EvacuateWhenUnrearmable : ConditionalTrait<EvacuateWhenUnrearmableInfo>, INotifyIdle
	{
		public EvacuateWhenUnrearmable(EvacuateWhenUnrearmableInfo info)
			: base(info) { }

		void INotifyIdle.TickIdle(Actor self)
		{
			if (IsTraitDisabled)
				return;

			// Bot airframes belong to their bot module unless explicitly opted in — see IncludeBotOwners.
			if (!Info.IncludeBotOwners && (self.Owner.IsBot || !self.Owner.Playable))
				return;

			// IsUnoccupiedAirframe, not IsIdle. Actor.IsIdle is never true for a hovering helicopter — a spent
			// heli holds FlyIdle forever — so a plain idle test on one is dead code (AIUtils.cs:45-52). INotifyIdle
			// still only fires with no current activity, so this additionally tolerates the FlyIdle hold that
			// ReturnToBase's no-resupplier branch leaves behind.
			if (!AIUtils.IsUnoccupiedAirframe(self))
				return;

			var totalPools = 0;
			var loadedPools = 0;
			foreach (var pool in self.TraitsImplementing<AmmoPool>())
			{
				totalPools++;
				if (pool.HasAmmo)
					loadedPools++;
			}

			var aircraft = self.TraitOrDefault<Aircraft>();
			var alreadyEvacuating = aircraft != null && aircraft.EvacuatingOffMap;

			// A Rearmable naming at least one host is what makes this airframe one the ruling covers. Read from
			// the RULES, not from the world: whether a pad is actually PRESENT is the separate host term below,
			// and conflating the two is what would fly the armed Chinook away (see AirframeEvacMath remarks).
			var rearmInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();
			var designedToRearm = rearmInfo != null && rearmInfo.RearmActors.Count > 0;

			if (AirframeEvacMath.Decide(totalPools, loadedPools, designedToRearm,
					AirframeReadiness.HasRearmHost(self), alreadyEvacuating)
				!= AirframeEvacAction.Evacuate)
				return;

			// Queued false so this cancels the FlyIdle hold. Same disposition and same handicap-adjusted refund the
			// "Evacuate" order reaches (DeliversCash@Rotation), so a helicopter that leaves this way is worth
			// exactly what one the player retired by hand is worth.
			self.QueueActivity(false, new RotateToEdge(self, true, self.GetEvacuationRefund()));
			self.ShowTargetLines();
		}
	}
}
