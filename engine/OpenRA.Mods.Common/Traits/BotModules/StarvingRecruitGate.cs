#region Copyright & License Information
/*
 * WW3MOD (@experimental) — the ammo term in unit tasking.
 *
 * DOCTRINE: "a platoon that is running low on ammo WILL DIE if they are not resupplied." The supply layer
 * already acts on that — SupplyFollowerBotModule will send a truck 30+ cells through believed danger for five
 * men below HuntStarvingThresholdPerMille. Nothing on the TASKING side read the same number, so the same five
 * men were recruited onto an attack axis and posted to an ambush lane while the truck was still driving to
 * them. Measured 2026-08-10 (WORKSPACE/bugs/discovered.md): a five-man platoon at 10/100 rounds pulled apart
 * from a 1-cell clump to a 9-cell one and streamed at believed artillery it had no ammunition to answer.
 *
 * THE RULE IS EXCLUSION, NOT DE-PRIORITISATION, and that is a decision rather than an implementation detail.
 * A weight gets overwhelmed exactly when the bot most wants bodies — an army under pressure, which is the same
 * army that is starving — so the one case where being wrong is fatal is the case a weight loses. A dry unit is
 * therefore unavailable for tasking until it is resupplied, and it rejoins the pool by itself the moment its
 * ammo climbs back: this predicate observes the pools every scan and holds no latch.
 *
 * WHY EXCLUSION CANNOT DEADLOCK ANYTHING. Every consumer already handles an empty or short pool — units die,
 * so "fewer units than I wanted" is the ordinary case on all of them: an under-strength axis is released, a
 * lane is not posted, a garrison waits, LayeredDefence returns early on an empty reserve. The existing
 * SkipOutOfAmmoUnits flag is the same shape of exclusion at the same call sites and has shipped since
 * 2026-07-21. Withholding is strictly weaker than the unit being dead, and nothing blocks on a dead unit.
 *
 * WHAT IT DOES NOT DO: it does not issue a Stop. A unit already carrying a move order finishes it and then
 * stands, because cancelling would also cancel AutoSeekSupplies — the walk to a dropped crate, which the
 * supply doctrine calls CORRECT behaviour (DOCS/reference/supply-route.md §"Infantry walking to a placed
 * crate"). What stops is the RE-tasking: nothing picks the unit up again while it is dry.
 *
 * DETERMINISM (influence-stack invariant): zero RNG, integer math only, delegating the threshold comparison to
 * SupplyHuntMath.BelowSeekThreshold so this and the supply layer cannot drift apart on the word "starving".
 * The held set is touched only by Add/Remove/Contains and never enumerated, so no log or decision depends on
 * hash order.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Per-module gate answering "is this unit too low on ammo to be given a job". One instance per bot module
	/// instance; the <paramref name="module"/> tag names the tasking that was refused in the log.
	/// </summary>
	public sealed class StarvingRecruitGate
	{
		readonly string module;
		readonly HashSet<uint> held = new HashSet<uint>();

		public StarvingRecruitGate(string module)
		{
			this.module = module;
		}

		/// <summary>
		/// Whether ANY servable ammo pool sits below the threshold. Any, not all: a rifleman out of rifle ammo
		/// is starving whether or not his RPG is full, matching SupplyFollowerBotModule's own cluster reading.
		/// A unit carrying no pool at all (infinite-ammo hulls) is never starving. thresholdPerMille &lt;= 0
		/// disables the gate outright, which is the shipped default on every consumer.
		/// </summary>
		public static bool IsStarving(Actor a, int thresholdPerMille)
		{
			if (thresholdPerMille <= 0)
				return false;

			foreach (var pool in a.TraitsImplementing<AmmoPool>())
				if (SupplyHuntMath.BelowSeekThreshold(pool.CurrentAmmoCount, pool.Info.Ammo, thresholdPerMille))
					return true;

			return false;
		}

		/// <summary>
		/// The call-site form: the predicate plus a one-line log on each TRANSITION, so a platoon sitting still
		/// reads as a decision rather than as the bot being passive. Logged on transition only — the eligibility
		/// sites run every scan, and a line per scan per unit per module would bury the event it exists to show.
		///
		/// A unit already WALKING TO A REARM SOURCE is withheld unconditionally, threshold or no threshold.
		/// That clause is not a tuning knob and must not become one: every tasking order in this codebase is
		/// issued with QueueActivity(false, …), which cancels the current activity — so re-tasking a unit that
		/// is mid-resupply does not merely reorder its priorities, it destroys the errand and sends an empty
		/// gun back at the enemy. Making it depend on thresholdPerMille would mean the disposition works on
		/// @experimental and silently self-cancels on @stable, where the threshold is 0.
		/// </summary>
		public bool Withhold(Actor a, int thresholdPerMille)
		{
			var seekingRearm = AmmoPool.IsSeekingRearm(a);
			if (seekingRearm || IsStarving(a, thresholdPerMille))
			{
				if (held.Add(a.ActorID))
					Log.Write("debug",
						$"[exp-ammo] withhold module={module} player={a.Owner.PlayerName} unit={a.Info.Name}#{a.ActorID}" +
						$" cell={a.Location} reason={(seekingRearm ? "resupplying" : "starving")}" +
						$" threshold={thresholdPerMille}pm tick={a.World.WorldTick}");

				return true;
			}

			if (held.Remove(a.ActorID))
				Log.Write("debug",
					$"[exp-ammo] release module={module} player={a.Owner.PlayerName} unit={a.Info.Name}#{a.ActorID}" +
					$" cell={a.Location} tick={a.World.WorldTick}");

			return false;
		}
	}
}
