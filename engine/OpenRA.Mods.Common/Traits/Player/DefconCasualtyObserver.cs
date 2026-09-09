#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

/*
 * The DEFCON 2 -> 1 trigger: "anything destroyed by enemy action ends DEFCON 2."
 *
 * WHY THIS TRAIT SITS ON THE PLAYER ACTOR AND NOT ON THE WORLD.
 *
 * INotifyKilled is the only hook that names the killer, and there is no World-actor delivery of it.
 * Health dispatches to exactly two places (Health.cs:233-236): the victim's own traits, and the
 * traits of `self.Owner.PlayerActor` -- resolved at :126 and re-resolved on owner change at :133.
 * The World actor is in neither list, so a World trait would never be called. The alternatives are
 * both worse: World.ActorRemoved carries no attacker at all, and putting the observer on every unit
 * template would mean one trait instance per actor plus a YAML edit per template.
 *
 * On the player actor it is one instance per player, it fires exactly ONCE per death (the victim has
 * exactly one owner), and `self` in Killed is the VICTIM rather than the player -- which is what
 * makes `e.Attacker == self` the self-inflicted test rather than a comparison against the player.
 */

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Watches this player's losses and reports the ones that end DEFCON 2 to " + nameof(DefconEscalation) + ".",
		"Attach to the Player actor. Inert unless the World actor carries " + nameof(DefconEscalation) + ".")]
	public class DefconCasualtyObserverInfo : TraitInfo
	{
		public override object Create(ActorInitializer init) { return new DefconCasualtyObserver(); }
	}

	public class DefconCasualtyObserver : INotifyCreated, INotifyKilled
	{
		DefconEscalation escalation;

		// THE CASUALTY RULE. This is the single place it lives; nothing else in the branch decides
		// what counts. Settled by the user: anything destroyed by enemy action ends DEFCON 2 --
		// structures count as well as units, accidents and friendly fire do not.
		//
		// 1. A killer is named and it is not the victim itself. `e.Attacker == victim` is clean and
		//    complete for the whole self-inflicted class: helicopter crashes, FallToEarth, scuttles,
		//    and the sacrificial capture / demolish / repair kills all arrive that way.
		//
		// 2. The killer is an ENEMY of the victim's owner. Nothing in AttackInfo flags friendly fire,
		//    so the relationship has to be asked for explicitly. NOTE THE SHIPPED PRECEDENT GETS THIS
		//    WRONG IN THE OTHER DIRECTION: UpdatesPlayerStatistics.Killed (PlayerStatistics.cs:369-371)
		//    filters null-or-self and nothing else, so it credits a friendly killer with the kill.
		//    Do not copy that filter here on the assumption it is the house rule.
		//
		// 3. No filter on what the victim IS. A destroyed building trips this exactly as a killed
		//    infantryman does -- if structures were exempt, a player could spend the whole phase
		//    flattening buildings and hold the match in its opening indefinitely.
		//
		// WHAT THE RULE ASKS FOR THAT THE CODE CANNOT YET DELIVER, stated rather than papered over:
		// "accidents do not count" should exclude being RUN OVER, and today it cannot. Passable
		// (Passable.cs:108) and Mine (Mine.cs:50) kill with LocomotorInfo.CrushDamageTypes, which is
		// `default` -- an empty BitSet (Locomotor.cs:81) -- and is set by no locomotor in this mod
		// (no CrushDamageTypes line exists anywhere under mods/ww3mod/). So a crush arrives as
		// Attacker = <enemy vehicle>, DamageTypes = {}, byte-identical to a weapon that declares no
		// damage types. AS THINGS STAND, BEING RUN OVER BY AN ENEMY VEHICLE WILL END DEFCON 2.
		// There is no heuristic here to guess at it, because any such guess would also swallow real
		// weapon kills.
		//
		// To exclude crushes later, ONE clause goes in at the marker below:
		//     if (e.Damage.DamageTypes.Contains(<the crush damage type>)) return false;
		// What has to land first is giving this mod's ground locomotors a CrushDamageTypes value in
		// mods/ww3mod/rules/world.yaml. That is a separate, deliberate change with its own blast
		// radius -- death animations and husk selection also read damage types -- and it is not this
		// branch's to make.
		public static bool IsQualifyingCasualty(Actor victim, AttackInfo e)
		{
			var attacker = e?.Attacker;

			// 1. A named killer that is not the victim.
			if (attacker == null || attacker == victim)
				return false;

			// <-- THE CRUSH CLAUSE GOES HERE once ground locomotors declare CrushDamageTypes.

			// 2. Enemy action only. Friendly fire and neutral-owned victims fall out here, because a
			//    combatant's relationship to a neutral player is Neutral rather than Enemy.
			if (victim.Owner == null || attacker.Owner == null)
				return false;

			// 3. No test on what the victim is -- a structure qualifies exactly as a unit does.
			return attacker.Owner.RelationshipWith(victim.Owner) == PlayerRelationship.Enemy;
		}

		void INotifyCreated.Created(Actor self)
		{
			// Resolved once here rather than per death. TraitOrDefault, not Trait: a map or scenario
			// that strips DefconEscalation from the World actor must leave this trait inert, not throw.
			escalation = self.World.WorldActor.TraitOrDefault<DefconEscalation>();
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			// `self` is the VICTIM, not the player actor -- see the file header.
			if (escalation == null || !IsQualifyingCasualty(self, e))
				return;

			escalation.ReportCasualty(self);
		}
	}
}
