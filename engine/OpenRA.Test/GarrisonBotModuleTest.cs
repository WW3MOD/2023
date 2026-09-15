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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// First coverage of any kind for GarrisonBotModule (audit item #15). Until now the only file with
	/// "Garrison" in its name under OpenRA.Test was PoiGarrisonTest, which tests a DIFFERENT module's
	/// arithmetic and never references this one — so anyone checking "is the AI garrison tested?" by
	/// grepping found a green fixture about something else.
	///
	/// <para>WHAT THIS DELIBERATELY DOES NOT DO: touch GarrisonBotModule.cs. The ordering logic needs a
	/// World, actors and a bot player, which no fixture here can build, and extracting a seam for it
	/// would edit a bot module another branch is currently working in. So this pins the two properties
	/// that are checkable from outside and that a reader gets WRONG most often — both of them recorded
	/// as surprises in the 2026-09-01 audit — and leaves the order path to the scenario layer.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonBotModuleTest
	{
		static MiniYaml PlayerTrait(string key)
		{
			var player = ModRulesYaml.AllRuleNodes()
				.Where(x => x.Node.Key == "Player")
				.Select(x => x.Node)
				.ToArray();

			Assert.That(player, Is.Not.Empty, "no `Player` node found in the rules — the AI wiring lives on it.");

			var trait = player
				.SelectMany(p => p.Value.Nodes)
				.FirstOrDefault(n => n.Key == key);

			Assert.That(trait, Is.Not.Null,
				$"`{key}` is not declared on the Player actor. The AI garrison capability is wired there " +
				"(rules/ai/ai.yaml); if it moved or was renamed, this fixture is pinning nothing.");

			return trait.Value;
		}

		[Test]
		public void TheGarrisonModuleIsLiveForBothShippedBotProfiles()
		{
			// The audit's most counter-intuitive finding: bots DO garrison, and both profiles run the SAME
			// instance rather than gated twins. Per CLAUDE.md that is settled policy and not to be
			// "fixed" — this exists so that if the wiring changes, the next benchmark baseline is re-taken
			// knowingly instead of a @stable behaviour shift being attributed to something else.
			var condition = ModRulesYaml.Child(PlayerTrait("GarrisonBotModule@defenses"), "RequiresCondition");

			Assert.That(condition, Is.EqualTo("enable-ai-any"),
				$"GarrisonBotModule@defenses is gated on '{condition}' rather than enable-ai-any, so which " +
				"bots garrison has changed.");

			var grantedTo = ModRulesYaml.Child(PlayerTrait("GrantConditionOnBotOwner@anyai"), "Bots");

			Assert.That(grantedTo, Is.EqualTo("experimental, stable"),
				$"enable-ai-any is granted to '{grantedTo}'. That is the set of bot profiles which garrison " +
				"buildings, and changing it changes @stable — the benchmark control — so it must be a " +
				"deliberate edit with a re-taken baseline, not a side effect.");
		}

		[Test]
		public void AnUnsetActorTypeListIsThePermissiveBranchAndTheModReliesOnIt()
		{
			// The trap. GarrisonActorTypes reads as an allowlist, and an empty one reads as "nothing is
			// eligible" — but the empty case is the PERMISSIVE branch: the module falls back to any
			// PassengerInfo holder. The mod never sets it, so the fallback is the shipped behaviour, and a
			// reader who assumes the module is inert because the list is empty has it exactly backwards.
			Assert.That(new GarrisonBotModuleInfo().GarrisonActorTypes, Is.Empty,
				"GarrisonActorTypes no longer defaults to empty. The mod deliberately leaves it unset and " +
				"relies on the empty-list fallback; a non-empty default silently narrows which units the " +
				"AI will garrison.");

			var shipped = ModRulesYaml.Child(PlayerTrait("GarrisonBotModule@defenses"), "GarrisonActorTypes");

			Assert.That(shipped, Is.Null,
				$"GarrisonBotModule@defenses now sets GarrisonActorTypes: '{shipped}'. That leaves the " +
				"permissive fallback and becomes a real allowlist — which is a behaviour change to both " +
				"bot profiles, and makes the trait's own [Desc] (which documents the fallback as the " +
				"shipped state) wrong.");
		}

		[Test]
		public void TheThreatGateIsWhatMakesItDefensiveRatherThanImmediate()
		{
			// RequireBelievedThreat is the single line separating "takes cover when something is coming"
			// from the pre-2026-08 behaviour where idle units were posted into an arbitrary rear house on
			// the bot's first tick and never came out. It reads the fog-legal believed-danger field, so it
			// is also the reason the module is fog-legal at all.
			var module = PlayerTrait("GarrisonBotModule@defenses");

			Assert.That(ModRulesYaml.Child(module, "RequireBelievedThreat"), Is.EqualTo("true"),
				"RequireBelievedThreat is no longer set, so the module has no enemy or danger term at all " +
				"— PrioritizeExposed is a sort comparator, never a filter. Bots will garrison on tick one.");

			Assert.That(ModRulesYaml.Child(module, "ReleaseWhenThreatClears"), Is.EqualTo("true"),
				"ReleaseWhenThreatClears is no longer set, so garrisoning becomes permanent again: units " +
				"enter a house and are never unloaded, which is how they leave the match rather than take " +
				"cover.");
		}
	}
}
