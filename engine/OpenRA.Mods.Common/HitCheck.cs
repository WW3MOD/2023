#region Copyright & License Information
/*
 * WW3MOD anomaly detector for "it fired and nothing died".
 *
 * This is a BUG DETECTOR, not a trace. It is silent unless a shot lands far below what its own
 * warhead says it should do. GunTrace (same directory) is the per-hit dump and stays env-gated;
 * this writes to its own hitcheck.log so it can be watched continuously without reading anything
 * else.
 *
 * WHY IT LIVES AT THE WARHEAD AND NOT ON INotifyDamage
 * AttackInfo carries only the FINAL Damage.Value (OpenRA.Game/Traits/TraitsInterfaces.cs:80-86).
 * A listener there sees "-192" and cannot tell a small weapon from a defeated one. The raw and
 * post-armour numbers coexist in exactly one scope, DamageWarhead.InflictDamage, which is why the
 * call site is there. It also means the detector reads the WARHEAD'S OWN written damage rather
 * than the armament's named weapon -- which is what keeps the HIMARS off this log: its armament
 * weapon says 50 while the payload it spawns says 36000, and comparing against the named weapon
 * would report the most expensive strike in the game as a catastrophic under-performer every time
 * it fired.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common
{
	/// <summary>
	/// Flags hits where armour defeated a shot that should have killed. See the header for why this
	/// cannot be done from INotifyDamage, and <see cref="IsUnderPerforming"/> for the predicate and
	/// the measurements behind each of its clauses.
	/// </summary>
	public static class HitCheck
	{
		/// <summary>
		/// Effective thickness below which a firing is routed to the quiet channel instead.
		///
		/// MEASURED, not guessed. Replaying the predicate over the whole shipped ruleset (every
		/// damage warhead x every armoured actor, 14274 pairs) produced 370 firings, and ALL 370 --
		/// not most, all -- were an area/splash companion warhead against a lightly-armoured
		/// airframe: tran (600 HP, effective 10), heli (800 HP, 20), littlebird (300 HP, 5). A tank
		/// round's splash not one-shotting a transport helicopter is the designed shape, not a
		/// defect. Every one of those victims sits at or under effective thickness 20; every real
		/// armour value a designer sizes penetration against is 150+. A floor of 50 separates the
		/// two populations cleanly with a wide margin on both sides.
		///
		/// The trade, stated plainly: an anti-air weapon that genuinely forgot its Penetration would
		/// land in the quiet channel rather than the loud one. That is deliberate -- 370 known-benign
		/// firings would get the whole detector switched off inside a week, which costs more than the
		/// hypothetical catch.
		/// </summary>
		public const int ArmourFloor = 50;

		/// <summary>
		/// Armour must have eaten at least this much for a firing to count -- delivered damage at or
		/// under 25% of what the warhead wrote. An RPG losing 29% frontally against an Abrams
		/// (6000 -> 4285) is the armour model working and must stay quiet.
		/// </summary>
		public const int MaxDeliveredPercent = 25;

		/// <summary>
		/// True when armour turned a lethal shot into a non-lethal one.
		///
		/// The severity axis is DELIBERATELY the change in outcome, not the amount of damage lost.
		/// "Damage lost" was tried first and is wrong: it fires on a 50 HP drone that the shot kills
		/// several times over anyway, because the absolute loss is large while nothing about the
		/// result changed. Requiring that the shot WOULD have killed and now does NOT is what makes
		/// the flag self-justifying -- there is no reading of it that is merely a balance opinion.
		///
		/// Pure integer arithmetic, no world state, no allocation. Ordered so the two cheap int
		/// tests run before the caller needs to look up the victim's health.
		/// </summary>
		/// <param name="rawDamage">Warhead damage before the armour reduction.</param>
		/// <param name="deliveredAfterArmour">Damage surviving ApplyPenetration.</param>
		/// <param name="effectiveThickness">Thickness * ArmorDirectionPercent / 100 -- the number
		/// ApplyPenetration actually compares against, so top-attack is already folded in. This is
		/// what stops the detector reporting the ATGM, whose Penetration 100 clears an Abrams roof
		/// of 70 and is correctly sized despite looking seven times under-sized against 700.</param>
		/// <param name="victimMaxHp">Victim's maximum health.</param>
		public static bool IsUnderPerforming(int rawDamage, int deliveredAfterArmour, int effectiveThickness, int victimMaxHp)
		{
			if (!ArmourIsWorthSizingAgainst(effectiveThickness))
				return false;

			return LostMostOfItsDamage(rawDamage, deliveredAfterArmour)
				&& OutcomeChanged(rawDamage, deliveredAfterArmour, victimMaxHp);
		}

		/// <summary>
		/// The same shot below <see cref="ArmourFloor"/>. Reported under a separate, quieter marker
		/// so the main signal is not poisoned by the splash-versus-airframe population.
		/// </summary>
		public static bool IsUnderPerformingAgainstThinArmour(int rawDamage, int deliveredAfterArmour, int effectiveThickness, int victimMaxHp)
		{
			if (ArmourIsWorthSizingAgainst(effectiveThickness) || effectiveThickness <= 0)
				return false;

			return LostMostOfItsDamage(rawDamage, deliveredAfterArmour)
				&& OutcomeChanged(rawDamage, deliveredAfterArmour, victimMaxHp);
		}

		public static bool ArmourIsWorthSizingAgainst(int effectiveThickness)
		{
			return effectiveThickness >= ArmourFloor;
		}

		/// <summary>
		/// Cheap pre-gate, pure ints. Public so the call site can run it BEFORE looking up the
		/// victim's Health trait -- this sits on the damage path of every shot in the game, and a
		/// trait lookup per hit to feed a detector that fires on almost nothing would be a real cost
		/// for no benefit. Everything past this point is rare by construction.
		/// </summary>
		public static bool LostMostOfItsDamage(int rawDamage, int deliveredAfterArmour)
		{
			// A warhead that penetrates is not interesting however big it is. Unarmoured victims
			// reach here with delivered == raw, because InflictDamage skips the branch at Thickness
			// 0 -- which is why the ~109 Penetration-less warheads aimed at infantry never fire.
			if (rawDamage <= 0 || deliveredAfterArmour >= rawDamage)
				return false;

			return deliveredAfterArmour * 100 / rawDamage <= MaxDeliveredPercent;
		}

		static bool OutcomeChanged(int rawDamage, int deliveredAfterArmour, int victimMaxHp)
		{
			if (victimMaxHp <= 0)
				return false;

			return rawDamage >= victimMaxHp && deliveredAfterArmour < victimMaxHp;
		}

		// One line per distinct (shooter, warhead, victim) combination per session. Without this the
		// log is unreadable within seconds -- the same weapon fires on the same target type
		// thousands of times a match, and a detector nobody can read is a detector nobody runs.
		static readonly HashSet<string> Reported = new HashSet<string>();

		/// <summary>
		/// Logging only -- reads no simulation state it does not already have and writes nothing
		/// back, so this cannot affect determinism or replay byte-identity.
		/// </summary>
		public static void Report(string shooter, string victim, string warheadType, int writtenDamage,
			int penetration, int rawDamage, int deliveredAfterArmour, int effectiveThickness, int victimMaxHp)
		{
			var thin = !ArmourIsWorthSizingAgainst(effectiveThickness);
			var key = $"{shooter}|{warheadType}|{writtenDamage}|{victim}";
			if (!Reported.Add(key))
				return;

			var marker = thin ? "[hitcheck-thin]" : "[HITCHECK]";
			Log.Write("hitcheck",
				$"{marker} {shooter} -> {victim}: {warheadType} wrote Damage={writtenDamage} Penetration={penetration}, " +
				$"armour {effectiveThickness} cut {rawDamage} to {deliveredAfterArmour} " +
				$"({deliveredAfterArmour * 100 / rawDamage}%) against {victimMaxHp} HP -- would have killed, did not. " +
				$"Size Penetration on this warhead, or confirm the reduction is intended.");
		}

		/// <summary>Test seam. Not called by the game.</summary>
		public static void ResetForTests()
		{
			Reported.Clear();
		}
	}
}
