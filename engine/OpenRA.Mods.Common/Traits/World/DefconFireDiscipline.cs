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
 * THE DEFCON FIRE RULES -- BOTH OF THEM -- as pure predicates with no dependency on Actor, World or
 * the lobby.
 *
 * Split out for exactly the reason DefconEscalationState is split out: nothing in OpenRA.Test can
 * construct a World, so a rule living inside a trait method is a rule verified by reading only. The
 * sites that consult these are all inside per-actor traits and are therefore NOT unit-testable; this
 * class is the part that is, and it holds the whole of both decisions.
 *
 * THERE ARE TWO RULES AT TWO RUNGS, AND THEY ARE NOT THE SAME SHAPE. Reading one as a stricter
 * version of the other is the mistake this header exists to prevent.
 *
 *   DEFCON 3, "Positioning" -- CeasesFire / PermitsWeapon. NO weapon fires. Not by autotarget, not
 *   by a player's explicit attack order, not by force-fire at bare ground. The only thing that fires
 *   is an armament that has explicitly opted out as not-really-a-weapon (the drone operator's
 *   targeter, the medic's Heal, the engineer's Repair, the mine-clearing charge). This rule is
 *   therefore a function of the ARMAMENT and not of who asked -- it takes no AttackSource at all.
 *
 *   DEFCON 2, "Weapons free" -- HoldsFire / Permits. Either side may attack anything, but units do
 *   not fire AUTONOMOUSLY: every shot is one a player gave. This rule is a function of PROVENANCE and
 *   nothing else, and it deliberately lets force-attacks through.
 *
 * So the two disagree about force-fire on purpose: at 2 a force-attack is the clearest possible
 * evidence that a human ordered the shot and it is exempt; at 3 it is the bypass the whole rule is
 * aimed at. DEFCON 1 is open war and neither applies.
 *
 * WHY THE DEFCON 3 RULE IS NOT EXPRESSED AS A STRICTER `Permits`. A decision recorded at DEFCON 2's
 * design time evaluated gating Armament and rejected it, on the grounds that it "catches everything
 * including force-attacks -- and forbids ordered fire too, which is the exact inverse of the rule".
 * That verdict is correct AND is scoped to DEFCON 2. At DEFCON 3 the requirement is "no fire at all",
 * so the property that made the Armament lever wrong there is precisely what makes it right here.
 *
 * WHAT "A SHOT SOMEBODY GAVE" MEANS. It is not defined here. AutoTarget.IsAutoAcquiredSource is
 * already the engine's own provenance test, and its docstring settles the bot case outright: a bot's
 * named-target Attack counts as a direct order, its AttackMove does not, because the player/bot
 * ordered a move and not that particular target. Permits below is a thin skin over that method
 * rather than a second, parallel definition that could drift from it -- and
 * DefconFireDisciplineTest.TheRuleIsExpressedInTheEngineOwnProvenanceTest pins the two together over
 * every AttackSource value, so adding a fourth source fails the build's tests rather than silently
 * defaulting it to "autonomous".
 *
 * WHAT IT DOES NOT COVER, AND THAT IS CORRECT. Explosions, mines, crushing, crash weapons and
 * vaporisation consult no stance and never reach a fire path at all. A unit dying to a mine at
 * DEFCON 2 is not somebody's shot; the rule is about fire, not about damage.
 */

namespace OpenRA.Mods.Common.Traits
{
	public static class DefconFireDiscipline
	{
		// The one rung the hold applies at. Derived from Ceiling rather than written as a literal 2 so
		// that "the middle rung of three" survives someone re-numbering the ladder.
		public const int HoldFireLevel = DefconEscalationState.Ceiling - 1;

		// THE POSITIONING RUNG, where no weapon fires at all. The ceiling itself: it is the level a
		// match opens at, the level the dividing wall stands at (DefconWall.ActiveLevels), and the one
		// the lobby labels "Positioning".
		public const int CeaseFireLevel = DefconEscalationState.Ceiling;

		/// <summary>Is the match at the rung where units may not fire of their own accord? False for
		/// every other level INCLUDING <see cref="DefconEscalationState.NoLevel"/>, which is what makes
		/// Skirmish a strict no-op without this class knowing what Skirmish is.</summary>
		public static bool HoldsFire(int level)
		{
			return level == HoldFireLevel;
		}

		/// <summary>Is the match at the rung where NOTHING fires, whoever ordered it? False for every
		/// other level including <see cref="DefconEscalationState.NoLevel"/>, on the same argument as
		/// <see cref="HoldsFire"/>: Skirmish never leaves NoLevel, so this is a single int compare that
		/// is false forever and the whole cease-fire is a strict no-op outside the mode.</summary>
		public static bool CeasesFire(int level)
		{
			return level == CeaseFireLevel;
		}

		/// <summary><para>May this ARMAMENT fire at this level? The cease-fire's whole decision, and
		/// deliberately NOT a function of provenance -- which is the one line that separates it from
		/// <see cref="Permits"/> below and the reason both live in this file.</para>
		///
		/// <para>At <see cref="HoldFireLevel"/> the rule is "only shots somebody ordered", so that
		/// predicate takes an <see cref="AttackSource"/> and exempts force-attacks. At
		/// <see cref="CeaseFireLevel"/> the rule is "no shots at all", so this one takes neither: an
		/// ordered attack, an attack-move contact shot and a force-fire at bare ground are refused
		/// identically. Force-fire-on-ground is the bypass this exists to close -- it is the one input a
		/// player can use to aim a weapon at terrain nobody is standing on, and a provenance test would
		/// wave it straight through.</para>
		///
		/// <para><paramref name="isInert"/> is the single carve-out, and it is an explicit per-armament
		/// opt-in (ArmamentInfo.FiresDuringCeaseFire) rather than anything inferred. A heuristic over
		/// damage cannot work in either direction here: the medic's Heal and the engineer's Repair carry
		/// NEGATIVE damage and must keep working, while IskanderTargeter and HIMARSTargeter carry
		/// Damage: 0 and are dummy triggers whose fire spawns a live ballistic missile through
		/// MissileSpawnerMaster's INotifyAttack.Attacking hook. Zero damage is therefore not evidence of
		/// an inert weapon, and negative damage is not evidence of a real one.</para></summary>
		public static bool PermitsWeapon(int level, bool isInert)
		{
			if (!CeasesFire(level))
				return true;

			return isInert;
		}

		/// <summary>May a shot with this provenance happen at this level? Everything is permitted at
		/// every level but <see cref="HoldFireLevel"/>; there, only what somebody ordered.</summary>
		public static bool Permits(int level, AttackSource source, bool forceAttack)
		{
			if (!HoldsFire(level))
				return true;

			// A force-attack is a direct order by construction -- it can only come from a player click,
			// the Lua binding or a deliberate bot order. It arrives with source Default today, so this
			// clause is belt-and-braces rather than load-bearing; it is written out because the reader
			// of a hold-fire rule needs to see that force-attacks are not caught by it.
			if (forceAttack)
				return true;

			return !AutoTarget.IsAutoAcquiredSource(source);
		}
	}
}
