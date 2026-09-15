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

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Both readouts on a garrisoned building — the occupancy pips (WithGarrisonDecoration) and the
	/// damage-state pips (^GarrisonHealthPips) — must be visible to the ENEMY, which is the one player
	/// neither was ever shown to.
	///
	/// <para>WithDecorationBaseInfo.ValidRelationships defaults to Ally (WithDecorationBase.cs:107-108),
	/// and garrisoning TRANSFERS ownership of the building to the entering player
	/// (GarrisonManager.cs:263-267). So the building's owner IS the garrisoning player, the opponent
	/// always evaluated to Enemy, and ShouldRender suppressed every pip (:165-170). The readout existed,
	/// was correct, and was drawn only for the player who already knew what was in his own building —
	/// while the attacker, for whom "is this house manned" is the whole decision, got nothing.</para>
	///
	/// <para>WHAT THIS FIXTURE CAN AND CANNOT SEE. It pins the shipped YAML and the predicate
	/// ShouldRender evaluates on it; it does not render a frame. Rendering is covered by the manager's
	/// screenshot pass. What makes the YAML assertion worth more than a string compare is that it
	/// parses the value with the same flags-enum semantics the engine uses and then asks the same
	/// question the engine asks — HasRelationship(Enemy) — so a value that parses to something
	/// unexpected fails here rather than at a player's screen.</para>
	///
	/// <para>RED: delete the `ValidRelationships` line from any one of the four
	/// WithGarrisonDecoration blocks (civilian.yaml, structures-defenses.yaml x3) and
	/// EveryGarrisonDecorationAdmitsTheEnemy fails naming that actor and file. The companion test
	/// TheEngineDefaultWouldStillHideTheEnemy is what proves that assertion is not vacuous: it pins
	/// the C# default at Ally-only, so without the YAML override the enemy really is excluded.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonOccupancyVisibilityTest
	{
		static PlayerRelationship Parse(string value, string where)
		{
			Assert.That(value, Is.Not.Null.And.Not.Empty,
				$"{where} sets no ValidRelationships, so it inherits the C# default of Ally and this " +
				"readout is invisible to the enemy.");

			// Enum.Parse handles the comma-separated flags spelling MiniYaml uses ("Ally, Enemy, Neutral").
			Assert.That(Enum.TryParse<PlayerRelationship>(value, true, out var parsed), Is.True,
				$"{where} has ValidRelationships: '{value}', which is not a PlayerRelationship. The " +
				"engine's FieldLoader would reject this at load, so the mod would not start.");

			return parsed;
		}

		/// <summary>The four nodes that declare the trait: ^CivBuilding (covering all 38 civilian
		/// actors by inheritance) plus GTWR, PBOX and HBOX, which each declare the garrison stack
		/// independently because they inherit ^Defense rather than ^CivBuilding.</summary>
		static (string File, MiniYamlNode Node)[] DecorationHolders()
		{
			var holders = ModRulesYaml.AllRuleNodes()
				.Where(x => ModRulesYaml.ChildNode(x.Node.Value, "WithGarrisonDecoration") != null)
				.ToArray();

			Assert.That(holders.Length, Is.EqualTo(4),
				"expected exactly 4 nodes to declare WithGarrisonDecoration (^CivBuilding, GTWR, PBOX, " +
				$"HBOX); found {holders.Length}: {string.Join(", ", holders.Select(h => h.Node.Key))}. A " +
				"garrison building added without one has no occupancy readout for anybody; one added " +
				"with a hand-written block will not have been checked by this fixture. Update the count " +
				"deliberately.");

			return holders;
		}

		[Test]
		public void EveryGarrisonDecorationAdmitsTheEnemy()
		{
			foreach (var (file, node) in DecorationHolders())
			{
				var deco = ModRulesYaml.ChildNode(node.Value, "WithGarrisonDecoration");
				var where = $"{node.Key} ({file})";
				var relationships = Parse(ModRulesYaml.Child(deco, "ValidRelationships"), where);

				Assert.That(relationships.HasRelationship(PlayerRelationship.Enemy), Is.True,
					$"{where} has ValidRelationships: '{relationships}', which excludes Enemy. Because " +
					"garrisoning transfers ownership to the garrisoning player, the opponent always " +
					"evaluates to Enemy — so this building's occupancy pips are invisible to the only " +
					"player who needs them, and an attacker's sole cue that a house is manned is being " +
					"shot from it.");
			}
		}

		[Test]
		public void TheOwnerAndThirdPartiesKeepSeeingIt()
		{
			// Guards the obvious wrong fix: writing `ValidRelationships: Enemy` and trading the
			// owner's own readout — which is what the garrison panel's shield and port rows are read
			// against — for the attacker's.
			foreach (var (file, node) in DecorationHolders())
			{
				var deco = ModRulesYaml.ChildNode(node.Value, "WithGarrisonDecoration");
				var where = $"{node.Key} ({file})";
				var relationships = Parse(ModRulesYaml.Child(deco, "ValidRelationships"), where);

				Assert.That(relationships.HasRelationship(PlayerRelationship.Ally), Is.True,
					$"{where} no longer admits Ally, so the garrisoning player has lost the occupancy " +
					"readout for his own building. Note Ally is also how a player relates to himself.");

				Assert.That(relationships.HasRelationship(PlayerRelationship.Neutral), Is.True,
					$"{where} no longer admits Neutral, so a third player who is neither ally nor enemy " +
					"sees nothing.");
			}
		}


		/// <summary>The ^GarrisonHealthPips template, which is where the damage-state rungs live. It is
		/// a single node carrying three WithDecoration traits, so unlike WithGarrisonDecoration it is
		/// found by name rather than by scanning for the trait.</summary>
		static MiniYamlNode HealthPipTemplate()
		{
			var node = ModRulesYaml.AllRuleNodes()
				.Where(x => x.Node.Key == "^GarrisonHealthPips")
				.Select(x => x.Node)
				.SingleOrDefault();

			Assert.That(node, Is.Not.Null,
				"^GarrisonHealthPips is not declared exactly once across the rules. It is the only place " +
				"the garrison damage rungs are configured, so this fixture can no longer see them.");

			return node;
		}

		static MiniYamlNode[] HealthPipRungs()
		{
			var rungs = HealthPipTemplate().Value.Nodes
				.Where(n => n.Key.StartsWith("WithDecoration@GarrisonHealth_", StringComparison.Ordinal))
				.ToArray();

			Assert.That(rungs.Length, Is.EqualTo(3),
				"expected exactly 3 WithDecoration@GarrisonHealth_ rungs (Light, Medium, Heavy); found " +
				$"{rungs.Length}: {string.Join(", ", rungs.Select(r => r.Key))}. A rung added without a " +
				"ValidRelationships line is invisible to the enemy; one removed is a damage step the " +
				"attacker can no longer read. Update the count deliberately.");

			return rungs;
		}

		[Test]
		public void EveryGarrisonHealthPipAdmitsTheEnemy()
		{
			// The damage rungs matter MORE to an enemy than to the owner, because Indestructible clamps
			// a garrison building at 1 HP instead of killing it: there is no collapse to read progress
			// off, so the damage state is the only signal that wearing the building down is working.
			foreach (var rung in HealthPipRungs())
			{
				var where = $"^GarrisonHealthPips/{rung.Key} (defaults.yaml)";
				var relationships = Parse(ModRulesYaml.Child(rung.Value, "ValidRelationships"), where);

				Assert.That(relationships.HasRelationship(PlayerRelationship.Enemy), Is.True,
					$"{where} has ValidRelationships: '{relationships}', which excludes Enemy. Because " +
					"garrisoning transfers ownership, the opponent always evaluates to Enemy — so an " +
					"attacker grinding a garrison down cannot see that anything is happening.");

				Assert.That(relationships.HasRelationship(PlayerRelationship.Ally), Is.True,
					$"{where} no longer admits Ally, so the garrisoning player has lost his own damage " +
					"readout.");

				Assert.That(relationships.HasRelationship(PlayerRelationship.Neutral), Is.True,
					$"{where} no longer admits Neutral.");
			}
		}

		[Test]
		public void WideningTheHealthPipsReachesGarrisonBuildingsAndNothingElse()
		{
			// ^GarrisonHealthPips is a shared template, so widening it is only safe while the set of
			// actors that inherit it IS the garrison set. If some unrelated structure starts inheriting
			// it, that structure silently starts showing its damage state to the enemy too.
			var inheritors = ModRulesYaml.AllRuleNodes()
				.Where(x => ModRulesYaml.Parents(x.Node).Contains("^GarrisonHealthPips"))
				.Select(x => x.Node.Key)
				.OrderBy(k => k, StringComparer.Ordinal)
				.ToArray();

			Assert.That(inheritors, Is.EqualTo(new[] { "GTWR", "HBOX", "PBOX", "^CivBuilding" }),
				"the set of actors inheriting ^GarrisonHealthPips changed to [" +
				string.Join(", ", inheritors) + "]. This template was widened to Enemy on the " +
				"understanding that only garrison buildings use it; anything else inheriting it now " +
				"reveals its damage state to opponents as a side effect.");
		}

		[Test]
		public void TheEngineDefaultWouldStillHideTheEnemy()
		{
			// THE VACUITY GUARD. Without this, the tests above would keep passing if the C# default
			// were widened to include Enemy — and they would no longer be evidence that the YAML is
			// doing the work. If this ever goes red because the default changed deliberately, the
			// YAML lines become redundant rather than wrong.
			var shipped = new WithGarrisonDecorationInfo();

			Assert.That(shipped.ValidRelationships.HasRelationship(PlayerRelationship.Enemy), Is.False,
				"WithDecorationBaseInfo.ValidRelationships now admits Enemy by default, so the explicit " +
				"lines on the four garrison buildings no longer prove anything and the assertions above " +
				"would pass with the YAML deleted.");

			Assert.That(shipped.ValidRelationships.HasRelationship(PlayerRelationship.Ally), Is.True,
				"the decoration default no longer admits Ally either — every decoration in the mod that " +
				"relies on the default has just gone invisible to its own owner.");

			// Same guard for the plain WithDecoration the three health rungs use. Different Info class,
			// same base default, and the ^GarrisonHealthPips assertions would go equally vacuous.
			var healthRung = new WithDecorationInfo();

			Assert.That(healthRung.ValidRelationships.HasRelationship(PlayerRelationship.Enemy), Is.False,
				"WithDecorationInfo now admits Enemy by default, so the three ValidRelationships lines on " +
				"^GarrisonHealthPips no longer prove anything.");
		}

		[Test]
		public void FogStillDecidesWhetherTheEnemySeesIt()
		{
			// Widening the audience must not have widened it past the fog. ShouldRender tests
			// World.FogObscures(self) BEFORE the relationship gate (WithDecorationBase.cs:138-140), so
			// the fog answer is inherited for free — but only for as long as this trait does not
			// override ShouldRender and skip it. That is what is asserted: the method the engine calls
			// is still the base one. A subclass that starts overriding it has to be re-read by hand,
			// and this test says so rather than letting a fog leak ship as a legibility improvement.
			var declared = typeof(WithGarrisonDecoration)
				.GetMethod("ShouldRender", BindingFlags.Instance | BindingFlags.NonPublic)
				?.DeclaringType;

			Assert.That(declared, Is.Not.Null,
				"WithDecorationBase.ShouldRender could not be found by reflection — it was renamed or " +
				"its visibility changed, and this fixture can no longer tell whether the fog gate runs.");

			Assert.That(declared, Is.EqualTo(typeof(WithDecorationBase<WithGarrisonDecorationInfo>)),
				$"WithGarrisonDecoration now overrides ShouldRender (declared on {declared}). The fog " +
				"check that makes enemy-visible occupancy honest lives in the base implementation, so " +
				"the override must be read to confirm it still calls World.FogObscures — otherwise a " +
				"remembered building under fog will report live occupancy to an enemy who cannot see it.");

			var healthDeclared = typeof(WithDecoration)
				.GetMethod("ShouldRender", BindingFlags.Instance | BindingFlags.NonPublic)
				?.DeclaringType;

			Assert.That(healthDeclared, Is.EqualTo(typeof(WithDecorationBase<WithDecorationInfo>)),
				$"WithDecoration now overrides ShouldRender (declared on {healthDeclared}), so the same " +
				"question has to be re-asked for the three ^GarrisonHealthPips rungs: a remembered " +
				"building under fog must not report its live damage state to an enemy who cannot see it.");
		}
	}
}
