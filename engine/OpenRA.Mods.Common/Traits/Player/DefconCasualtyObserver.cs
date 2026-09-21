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

using System.Collections.Generic;
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

	public class DefconCasualtyObserver : INotifyCreated, INotifyKilled, INotifyDamage
	{
		DefconEscalation escalation;

		// ==== WHO LAST SHOT THIS UNIT, FOR THE BURNOUT CASE ======================================
		// EVERY VEHICLE IN THIS MOD BLEEDS TO DEATH ON ITS OWN, AND THE BLEED IS SELF-INFLICTED.
		// `^EffectsWhenDamagedVehicles` carries `ChangesHealth@CriticalDamage` with
		// `PercentageStep: -1, Delay: 5, StartIfBelow: 50` (vehicles.yaml:183-188), and
		// ChangesHealth.cs:86 is literally `self.InflictDamage(self, ...)`. So any vehicle taken
		// below half health loses 1% of its maximum every 5 ticks until it dies -- and if no further
		// enemy round lands first, the FINISHING BLOW is attributed to the victim itself.
		//
		// MEASURED, run 260921_181456: an abrams shot a 40000-hp t90 down and the debug line read
		//     DEFCON casualty at level 2: t90(Russia) killed by t90(Russia) -- REJECTED: self-inflicted
		// while the SAME scenario with an 8000-hp t90 (run 260921_181057) read `-- qualifies`, because
		// one round from full health overkills it and the burn never starts. The rule was therefore
		// keyed on whether the victim happened to survive the first hit.
		//
		// IN PLAY THAT IS THE COMMON CASE, NOT AN EDGE: a player who shoots an enemy tank below half
		// and lets it burn out has taken a life by anybody's reading, and DEFCON 2 -- the phase whose
		// whole premise is "the first casualty is always somebody's decision" -- did not end.
		//
		// STRINGS, NOT Actor REFERENCES, and keyed on ActorID. Holding the attacker would keep a dead
		// actor alive for the rest of the match and read `.Owner` off a disposed one; the two strings
		// are all ReportCasualty ever wanted. Entries are added on enemy damage and removed on death.
		// A unit damaged and never killed leaves one behind -- bounded by the units this player has
		// had damaged in one match, two short strings each, and never iterated.
		//
		// DETERMINISTIC: lookups and inserts only, never an enumeration, so no client can order this
		// differently from another. Not [Sync] -- it is derived from synced events rather than an
		// input to one.
		readonly Dictionary<uint, (string Type, string Owner)> lastEnemyDamager = new();

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

		/// <summary>
		/// Does a death the DIRECT rule rejects still count, because enemy action is what put the
		/// victim in the state that finished it?
		/// </summary>
		// NARROW ON PURPOSE, AND THE NARROWNESS IS THE WHOLE ARGUMENT. This fires ONLY when the
		// finishing blow is SELF-INFLICTED -- the burnout and the death cook-off -- and never when it
		// is friendly fire or a neutral. Clause 2 of the direct rule ("enemy action only") is
		// therefore untouched: a unit an ally finishes off still does not count, however much enemy
		// damage it took first, because that is a different question the user has already ruled on.
		//
		// NO TIME WINDOW, deliberately. A burn only ever starts from damage, so a vehicle that bleeds
		// out was necessarily driven there; adding "within N seconds of the last enemy hit" would add
		// a tunable nobody has judged in play, and every value of it would be a guess. If a match
		// ever produces a burnout minutes after its last enemy contact, that is the thing to revisit.
		public static bool QualifiesByPriorEnemyDamage(bool directlyQualifies, bool selfInflicted, bool hadPriorEnemyDamage)
		{
			return !directlyQualifies && selfInflicted && hadPriorEnemyDamage;
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			// `self` is the VICTIM -- Health dispatches this list exactly as it dispatches Killed
			// (Health.cs:122,275-276), so this trait sees every point of damage taken by every actor
			// its player owns, which is precisely the set it may later be asked about.
			if (escalation == null)
				return;

			var attacker = e?.Attacker;

			// HEALS AND SELF-HARM ARE NOT A DAMAGER. A negative Damage.Value is a medic or an
			// engineer, and the self-inflicted burn is the very thing this record exists to look
			// PAST -- recording it would make every burning vehicle its own last enemy damager.
			if (attacker == null || attacker == self || e.Damage.Value <= 0)
				return;

			if (self.Owner == null || attacker.Owner == null
				|| attacker.Owner.RelationshipWith(self.Owner) != PlayerRelationship.Enemy)
				return;

			lastEnemyDamager[self.ActorID] = (attacker.Info.Name, attacker.Owner.InternalName);
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
			if (escalation == null)
				return;

			var direct = IsQualifyingCasualty(self, e);
			var attackerActor = e?.Attacker;
			var selfInflicted = attackerActor != null && attackerActor == self;

			// The record is consumed here whatever the verdict, so a death always clears its entry.
			var hadPrior = lastEnemyDamager.TryGetValue(self.ActorID, out var prior);
			lastEnemyDamager.Remove(self.ActorID);

			var byPrior = QualifiesByPriorEnemyDamage(direct, selfInflicted, hadPrior);
			var qualifies = direct || byPrior;

			// ---- WHY A DEATH DID OR DID NOT END DEFCON 2 --------------------------------------
			// ADDED 2026-09-21 after run 260921_171955, where a t90 was killed by an enemy abrams at
			// DEFCON 2 and the level did not move -- and NOTHING anywhere said why. Every input to
			// IsQualifyingCasualty is reconstructible only from inside this method: the attacker is
			// gone from the log by the time anyone looks, and the four rejection clauses are
			// indistinguishable from outside ("the level is still 2" is all any observer can see).
			// Four hours of reading the dispatch, the relationship setup and the death path could not
			// discriminate between them; one line here does.
			//
			// GATED ON THE HOLD-FIRE RUNG, so this is at most a handful of lines in a real match --
			// deaths at DEFCON 3 cannot happen (nothing fires) and deaths at 1 are not listened for.
			// It is the only rung where the answer is interesting, because it is the only rung where
			// a death is supposed to DO something.
			if (DefconFireDiscipline.HoldsFire(escalation.Mode, escalation.Level))
			{
				var attacker = attackerActor;
				var reason = direct ? "qualifies"
					: byPrior ? $"qualifies BY PRIOR ENEMY DAMAGE ({prior.Type} of {prior.Owner}); "
						+ "the finishing blow was the victim's own burnout"
					: attacker == null ? "REJECTED: no attacker named"
					: attacker == self ? "REJECTED: self-inflicted (attacker == victim), and no enemy "
						+ "had damaged it"
					: self.Owner == null || attacker.Owner == null ? "REJECTED: an owner is null"
					: "REJECTED: not enemy action, relationship is "
						+ attacker.Owner.RelationshipWith(self.Owner);

				Log.Write("debug",
					$"DEFCON casualty at level {escalation.Level}: {self.Info.Name}" +
					$"({self.Owner?.InternalName ?? "<null>"}) killed by " +
					$"{attacker?.Info.Name ?? "<none>"}({attacker?.Owner?.InternalName ?? "<none>"}) -- {reason}.");
			}

			if (!qualifies)
				return;

			// The ATTACKER goes through too (§B6): the event-log line that goes with the combined
			// banner names who fired, and this handler is the only place in the branch that knows.
			// On the burnout path that is the unit that SHOT it, not the unit itself -- naming the
			// victim as its own killer in the match record would be worse than naming nobody.
			if (direct)
				escalation.ReportCasualty(self, attackerActor.Info.Name, attackerActor.Owner.InternalName);
			else
				escalation.ReportCasualty(self, prior.Type, prior.Owner);
		}
	}
}
