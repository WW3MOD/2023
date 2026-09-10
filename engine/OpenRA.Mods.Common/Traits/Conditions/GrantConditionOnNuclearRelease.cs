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
 * The nuclear release ladder's YAML face: one condition per RELEASED BAND, granted cumulatively.
 *
 * This is GrantConditionOnDefconLevel's sibling and works the same way -- it polls one int per
 * player per tick and grants and REVOKES as the ladder moves, which is the half of the machinery
 * that GrantConditionOnLobbyOption does not have (it grants once in Created and can never take it
 * back). The consumer side needs nothing: SupportPowerInstance recomputes `instancesEnabled` from
 * `Instances.Any(i => !i.IsTraitDisabled)` every tick (SupportPowerManager.cs:246), so a revoke
 * reaches a nuclear cameo AND its buy-tab entry on the next tick with nothing else being written.
 *
 * ==== CUMULATIVE, WHERE THE DEFCON FAMILY IS EXCLUSIVE, AND THAT IS A DELIBERATE DIVERGENCE ====
 * GrantConditionOnDefconLevel grants exactly ONE condition at a time and its header argues against
 * a cumulative family: "it is expressible with `||` today, and a second, overlapping family of
 * condition names is the kind of thing that gets out of step." That reasoning holds at three levels
 * and three consumers. It does not hold here. This ladder has SIX rungs -- HOLD plus five yield
 * bands since the 50/100 kt split of 2026-09-10 -- and THIRTEEN consumers, so the exclusive form
 * would put a five-way `||` on the lowest-yield weapons and a chain that has to be re-checked every
 * time a rung is added --
 *     RequiresCondition: nuclear-rung-1 || nuclear-rung-2 || nuclear-rung-3 || nuclear-rung-4 || nuclear-rung-5
 * against the cumulative form's
 *     RequiresCondition: nuclear-release-1kt
 * The second says what it means ("the 1 kt band is released") and cannot fall out of step with a
 * new rung, because a band is a property of the WEAPON and not of the ladder's length.
 *
 * THE SPLIT IS THE WORKED PROOF OF THAT. Adding a rung between 20 kt and 100 kt renumbered
 * GameEnder from 4 to 5 and changed the condition on exactly the two weapons whose BAND changed
 * (@B61Max and @RuKinzhalN, both 50 kt). Every other consumer -- including the 1 kt pair at the
 * bottom, furthest from the edit -- was untouched. Under the exclusive form all thirteen would have
 * needed re-reading, and the ones that were wrong would have been wrong silently.
 *
 * ==== THE POLARITY, WHICH BUYS TWO PROPERTIES AT ONCE ====
 * The conditions are POSITIVE and permissive: a power is gated on the band that releases it, so a
 * player may fire only what is granted. That is the opposite polarity to the mod's lobby gates
 * (`Condition: X-disabled`, feature `RequiresCondition: !X-disabled`) and it is right here for a
 * different reason than theirs. The lobby rule exists because an UNREGISTERED lobby option must
 * fail safe to off. A condition has no such fallback -- an ungranted name is simply false -- so the
 * positive form already fails safe: strip this trait and every nuclear power goes dark.
 *
 * ==== SKIRMISH IS NO LONGER A STRICT NO-OP HERE, AND THAT IS THE POINT OF THE CHANGE ====
 * THIS SECTION USED TO SAY the opposite: "Outside DEFCON Escalation this trait grants EVERY band on
 * the first tick and never moves again." That was true when it was written and is now false, because
 * the thing it protected turned out to be a LIE THE LOBBY WAS TELLING. The shipped timeline draws an
 * amber band captioned NUCLEAR WEAPONS PURCHASABLE from ten minutes in, and in Skirmish -- the
 * DEFAULT mode -- every nuclear power was on sale from the first second. The user's ruling
 * (decision 22) was to make the bar true rather than trim it.
 *
 * So outside Escalation the released rung now comes from NuclearUnlockClock, which is Skirmish's own
 * clock: bands come up FOR SALE one interval apart, ten minutes apart by default. See that file for
 * the three ways it is suspended -- Escalation, Sandbox, and an interval of 0 -- in each of which it
 * reports the top of the ladder and this trait behaves exactly as it did before.
 *
 * WHAT THE OLD PARAGRAPH WAS RIGHT ABOUT, AND WHAT KEEPS IT SATISFIED. Its warning was concrete: a
 * restrictive default here "would have switched off six weapons in demo-nuke-arsenal and reported it
 * as nothing more than a scenario that stopped firing." Nine scenarios under tools/autotest/scenarios
 * fire nuclear powers and all of them fire inside the first three minutes, so that hazard is real and
 * unchanged. It is answered rather than accepted: EIGHT of the nine set
 * PowersSandboxCheckboxEnabled: true, and Sandbox suspends the clock, so they are exempt with no edit
 * to any of them. The ninth (test-tacnuke-lobby-gated-off) never needed an exemption -- its positive
 * control is the Kinzhal, which is conventional and carries no NuclearYieldTons, so no band gates it.
 * A future scenario that fires a nuke WITHOUT sandbox is the case to watch: it must set the interval
 * to 0 or it will go quiet exactly as that warning describes.
 *
 * THE TSAR BOMBA IS UNTOUCHED. UnrestrictedCondition is still granted whenever the mode is not
 * Escalation, on the first tick, regardless of the clock -- decision 04 keeps that weapon out of
 * normal play by a route no schedule and no ceiling can reach, and the clock does not participate.
 */

using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Grants a condition for each nuclear yield band that is currently released, and revokes them",
		"as the ladder moves. Attach to the Player actor.",
		"",
		"TWO SOURCES, ONE PER GAME MODE, and they are different mechanisms rather than two settings of",
		"one: in DEFCON Escalation the rung comes from " + nameof(DefconEscalation) + "'s pressure",
		"ladder, which is CLIMBED BY FIRING; outside it the rung comes from " + nameof(NuclearUnlockClock),
		", which is a CLOCK and makes bands purchasable on an interval. With neither trait on the World",
		"actor every band is granted, which is the pre-ladder behaviour.")]
	public class GrantConditionOnNuclearReleaseInfo : TraitInfo
	{
		[GrantedConditionReference]
		[Desc("Condition granted while that ladder rung's band is released. The key is the rung.",
			"CUMULATIVE: every band at or below the current rung is held at once, so a weapon names",
			"only its own band. A rung with no entry grants nothing, which is how a rung is left",
			"unwired. Rungs are " + nameof(NuclearRung) + " values; 0 (HOLD) releases nothing and",
			"deliberately has no entry.")]
		public readonly Dictionary<int, string> Conditions = new Dictionary<int, string>
		{
			{ (int)NuclearRung.Kiloton, "nuclear-release-1kt" },
			{ (int)NuclearRung.TwentyKiloton, "nuclear-release-20kt" },
			{ (int)NuclearRung.FiftyKiloton, "nuclear-release-50kt" },
			{ (int)NuclearRung.HundredKiloton, "nuclear-release-100kt" },
			{ (int)NuclearRung.GameEnder, "nuclear-release-gameender" },
		};

		[GrantedConditionReference]
		[Desc("Condition granted ONLY outside DEFCON Escalation -- i.e. in Skirmish, in Sandbox, and",
			"when the World actor carries no " + nameof(DefconEscalation) + " at all.",
			"",
			"It is what the Tsar Bomba is gated on, and it is the whole of decision 04's ruling that",
			"the weapon is 'kept in code but cannot be used in game for now (keep it for sandbox)'.",
			"No ceiling setting and no number of detonations can grant this inside Escalation, so a",
			"50 Mt warhead is unreachable in normal play by construction rather than by a threshold",
			"someone could tune past.",
			"",
			"It is ALSO what keeps Skirmish a strict no-op for that one weapon, which is why it is",
			"granted there and not only in Sandbox: demo-nuke-arsenal fires the Tsar Bomba and runs",
			"in Skirmish.")]
		public readonly string UnrestrictedCondition = "nuclear-release-unrestricted";

		public override object Create(ActorInitializer init) { return new GrantConditionOnNuclearRelease(this); }
	}

	public class GrantConditionOnNuclearRelease : INotifyCreated, ITick
	{
		readonly GrantConditionOnNuclearReleaseInfo info;
		readonly List<int> tokens = new List<int>();

		DefconEscalation escalation;
		NuclearUnlockClock unlockClock;
		int heldRung = -1;
		bool heldUnrestricted;

		public GrantConditionOnNuclearRelease(GrantConditionOnNuclearReleaseInfo info)
		{
			this.info = info;
		}

		/// <summary>Every band condition released at a rung. The pure part, so a test can reach it.</summary>
		// Cumulative and ORDERED BY RUNG, so the returned sequence is stable rather than dictionary
		// order -- a grant list that reordered itself between ticks would churn tokens for nothing.
		public static IEnumerable<string> ConditionsFor(int rung, IReadOnlyDictionary<int, string> conditions)
		{
			if (conditions == null)
				yield break;

			for (var r = NuclearReleaseLadder.Lowest; r <= rung && r <= NuclearReleaseLadder.Highest; r++)
				if (conditions.TryGetValue(r, out var condition) && !string.IsNullOrEmpty(condition))
					yield return condition;
		}

		void INotifyCreated.Created(Actor self)
		{
			escalation = self.World.WorldActor.TraitOrDefault<DefconEscalation>();
			unlockClock = self.World.WorldActor.TraitOrDefault<NuclearUnlockClock>();

			// Applied immediately so the opening bands are held from the first tick rather than one
			// tick in -- which matters here in a way it does not for DEFCON, because a power's cameo
			// is filtered on the very first Tick of SupportPowerInstance.
			Apply(self);
		}

		// Polling two values per player per tick, for the same reason GrantConditionOnDefconLevel
		// polls one: no creation-order dependency between the World actor and the player actors, and
		// no way to miss an edge. The body returns on the first comparison in every tick where
		// nothing moved, which is every tick of every match that never fires a nuke.
		void ITick.Tick(Actor self)
		{
			Apply(self);
		}

		void Apply(Actor self)
		{
			// A stripped DefconEscalation reads as "not Escalation", i.e. Skirmish. See the file
			// header: that is the pre-ladder behaviour, and it is what keeps a scenario that strips
			// World traits working exactly as it did.
			var unrestricted = escalation == null || escalation.Mode != DefconGameMode.Escalation;

			// OUTSIDE ESCALATION THE CLOCK DECIDES, and a MISSING clock still grants everything --
			// which is what makes registering NuclearUnlockClock the whole of the behaviour change,
			// and un-registering it the whole of the revert. A scenario or map that strips the trait
			// gets the pre-clock Skirmish match back with no other edit.
			var rung = unrestricted
				? unlockClock?.ReleasedRung ?? NuclearReleaseLadder.Highest
				: escalation.NuclearRungFor(self.Owner);

			if (rung == heldRung && unrestricted == heldUnrestricted)
				return;

			heldRung = rung;
			heldUnrestricted = unrestricted;

			// Revoke everything and re-grant. The ladder moves at most four times in a match, so the
			// cost is irrelevant and the alternative -- diffing two sets of tokens -- is where a
			// leak would hide.
			foreach (var token in tokens)
				self.RevokeCondition(token);

			tokens.Clear();

			foreach (var condition in ConditionsFor(rung, info.Conditions))
				tokens.Add(self.GrantCondition(condition));

			if (unrestricted && !string.IsNullOrEmpty(info.UnrestrictedCondition))
				tokens.Add(self.GrantCondition(info.UnrestrictedCondition));
		}
	}
}
