#region Copyright & License Information
/*
 * WW3MOD Logistics Center supplies soldiers — predicate pin + corpus pin.
 *
 * USER REQUEST, 2026-08-22: "The logistics center and logistics center MCV (Undeployed) should have a
 * supplies bar just like the truck, and should be able to rearm soldiers just like a supply truck."
 *
 * WHAT WAS ACTUALLY WRONG, because it is not what it looks like. The Centre ALREADY rearmed soldiers
 * and ALREADY had a bar: ProximityExternalCondition@ReplenishSoldiers grants replenish-soldiers at
 * 4c0 and every infantry ReloadAmmoPool is gated on it, and SupplyProvider has implemented
 * ISelectionBar since before the Centre was given the trait. The single thing that was not "like the
 * truck" is that the Centre's soldier rearm was FREE — a trickle that never touched the 3000 — so the
 * bar could not move for infantry. Metering it is the user's call, taken 2026-08-22.
 *
 * THE TRAP THIS FILE EXISTS TO PIN. The obvious fix is to widen SupplyProvider.RearmCondition from
 * replenish-vehicles to also accept replenish-soldiers. That fix does nothing, and it does nothing
 * silently: IsValidTarget checks DockedCondition BEFORE RearmCondition, and only ^Vehicle declares
 * unit.docked (vehicles.yaml:29). A soldier is rejected by the dock gate before his rearm condition is
 * ever looked at. Widening the condition would have changed a test that never runs — the shape
 * conventions.md calls "a change believed made, documented as made, and inert". MatchClientele exists
 * so that ordering is pinned by something other than a comment.
 *
 * The corpus half reads shipped YAML rather than a fixture because the thing being protected is the
 * corpus: the wiring is four keys across two files and every one of them fails silently if dropped.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class LogisticsCenterSupplyTest
	{
		// ---------------------------------------------------------------------------------
		// Predicate half: the clientele matcher, in isolation.
		// ---------------------------------------------------------------------------------

		[TestCase(true, TestName = "Soldier is served by the aura arm even though the dock gate rejects him")]
		[TestCase(false, TestName = "Soldier is refused when no aura arm is configured")]
		public void DockGateDoesNotReachTheAuraClientele(bool auraConfigured)
		{
			// A soldier at a Logistics Center: inside both radii, declares replenish-soldiers, does NOT
			// declare unit.docked and therefore cannot be holding it.
			var match = SupplyProvider.MatchClientele(
				inPrimaryRange: true,
				dockGateConfigured: true,
				targetIsDocked: false,
				targetDeclaresPrimaryCondition: false,
				auraConfigured: auraConfigured,
				inAuraRange: true,
				targetDeclaresAuraCondition: true);

			Assert.That(match.Matched, Is.EqualTo(auraConfigured),
				"A soldier must be servable exactly when the aura clientele is configured. If this fails "
				+ "with auraConfigured=true, the dock gate is still swallowing him — which is the whole "
				+ "defect: widening RearmCondition alone never reaches the check that rejects him.");

			if (auraConfigured)
				Assert.That(match.IsAura, Is.True, "He must be served on AURA terms (4c0, no dock), not dock terms.");
		}

		[Test]
		public void DockedVehicleStillTakesThePrimaryArm()
		{
			// The regression that would hurt most: a vehicle that docked for 2c0 service must not be
			// reclassified onto the wider, faster aura terms just because an aura arm now exists.
			var match = SupplyProvider.MatchClientele(
				inPrimaryRange: true,
				dockGateConfigured: true,
				targetIsDocked: true,
				targetDeclaresPrimaryCondition: true,
				auraConfigured: true,
				inAuraRange: true,
				targetDeclaresAuraCondition: true);

			Assert.That(match.Matched, Is.True);
			Assert.That(match.IsAura, Is.False, "Primary must win when both arms would accept the target.");
		}

		[Test]
		public void UndockedVehicleIsStillRefused()
		{
			// The property the dock gate exists for. A vehicle that has not docked declares
			// replenish-vehicles but not replenish-soldiers, so neither arm may take it.
			var match = SupplyProvider.MatchClientele(
				inPrimaryRange: true,
				dockGateConfigured: true,
				targetIsDocked: false,
				targetDeclaresPrimaryCondition: true,
				auraConfigured: true,
				inAuraRange: true,
				targetDeclaresAuraCondition: false);

			Assert.That(match.Matched, Is.False,
				"Adding the aura arm must not become a back door around the docking requirement.");
		}

		[Test]
		public void SoldierOutsideTheAuraRadiusIsRefused()
		{
			var match = SupplyProvider.MatchClientele(
				inPrimaryRange: false,
				dockGateConfigured: true,
				targetIsDocked: false,
				targetDeclaresPrimaryCondition: false,
				auraConfigured: true,
				inAuraRange: false,
				targetDeclaresAuraCondition: true);

			Assert.That(match.Matched, Is.False);
		}

		[Test]
		public void SingleClienteleProviderIsUnchanged()
		{
			// TRUK and SUPPLYCACHE: no dock gate, no aura arm. Their behaviour must be byte-identical to
			// the pre-change primary path, which is what makes this an additive change.
			var match = SupplyProvider.MatchClientele(
				inPrimaryRange: true,
				dockGateConfigured: false,
				targetIsDocked: false,
				targetDeclaresPrimaryCondition: true,
				auraConfigured: false,
				inAuraRange: false,
				targetDeclaresAuraCondition: false);

			Assert.That(match.Matched, Is.True);
			Assert.That(match.IsAura, Is.False);
		}

		// ---------------------------------------------------------------------------------
		// Corpus half: the shipped YAML actually carries the wiring.
		// ---------------------------------------------------------------------------------

		const string SoldierCondition = "replenish-soldiers";

		static string FindRules(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var parts = new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(relative).ToArray();
				var candidate = Path.Combine(parts);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/" + string.Join("/", relative));
		}

		static Dictionary<string, string> TraitFields(string file, string actorName, string traitKey)
		{
			var actor = MiniYaml.FromFile(FindRules(file.Split('/')))
				.FirstOrDefault(n => string.Equals(n.Key, actorName, StringComparison.OrdinalIgnoreCase));

			Assert.That(actor, Is.Not.Null, $"{actorName} is not defined in {file}.");

			var trait = actor.Value.Nodes.FirstOrDefault(n => n.Key == traitKey);
			Assert.That(trait, Is.Not.Null,
				$"{actorName} ({file}) declares no `{traitKey}`. Without it the actor has no supply pool, "
				+ "and therefore no supplies bar either — SupplyProvider is what implements ISelectionBar.");

			return trait.Value.Nodes.ToDictionary(n => n.Key, n => n.Value.Value);
		}

		[Test]
		public void LogisticsCenterMetersSoldiersFromItsOwnPool()
		{
			var fields = TraitFields("ingame/structures.yaml", "LOGISTICSCENTER", "SupplyProvider");

			Assert.That(fields.TryGetValue("AuraRearmCondition", out var aura) ? aura : null,
				Is.EqualTo(SoldierCondition),
				"LOGISTICSCENTER must serve soldiers through the AURA arm. Setting RearmCondition to "
				+ "replenish-soldiers instead would be inert: DockedCondition is checked first and only "
				+ "^Vehicle declares unit.docked, so the soldier never reaches the condition test.");

			Assert.That(fields.ContainsKey("DockedCondition"), Is.True,
				"The dock gate is what makes the second arm necessary; if it has gone, collapse the two arms.");
		}

		[Test]
		public void DrivingAndDeployedFormsCarryTheSameCapacity()
		{
			// USER RULING 2026-08-22: "There is no difference between when it is driving or when it is
			// deployed, it carries the supplies it carries." ITransformActorInitModifier TRANSFERS the
			// remainder across the deploy, so if the two capacities ever diverge the bar jumps at the
			// moment of deploy and a full MCV becomes a partial Centre (or overfills one).
			var centre = TraitFields("ingame/structures.yaml", "LOGISTICSCENTER", "SupplyProvider");
			var mcv = TraitFields("ingame/vehicles.yaml", "LCCV", "SupplyProvider");

			Assert.That(mcv["TotalSupply"], Is.EqualTo(centre["TotalSupply"]),
				"LCCV and LOGISTICSCENTER must declare the SAME TotalSupply. They are one thing in two "
				+ "postures, and the deploy is a plain transfer with no top-up.");

			Assert.That(int.Parse(centre["TotalSupply"]), Is.EqualTo(2250),
				"2250 is the user's figure — 3x the supply truck's 750. This is a REDUCTION from the "
				+ "old 3000 and is deliberate.");
		}

		[Test]
		public void SupplyCreditValueTracksCapacityNotCost()
		{
			// The mod prices supply 1:1 with credits — TRUK is TotalSupply 750 / SupplyCreditValue 750.
			// The Centre's old SupplyCreditValue of 3000 equalled its old TotalSupply of 3000, NOT its
			// Cost of 3500; the near-match with cost was coincidence and must not be re-derived from it.
			var centre = TraitFields("ingame/structures.yaml", "LOGISTICSCENTER", "SupplyProvider");

			Assert.That(centre["SupplyCreditValue"], Is.EqualTo(centre["TotalSupply"]),
				"SupplyCreditValue is the credit worth of a FULL LOAD, so it tracks TotalSupply.");
		}

		[Test]
		public void UndeployedMcvCarriesSuppliesAndServesSoldiers()
		{
			var fields = TraitFields("ingame/vehicles.yaml", "LCCV", "SupplyProvider");

			Assert.That(fields.TryGetValue("RearmCondition", out var cond) ? cond : null,
				Is.EqualTo(SoldierCondition),
				"The undeployed MCV serves soldiers on the PRIMARY arm — it has no dock gate, so it needs "
				+ "no second clientele.");

			Assert.That(fields.ContainsKey("DockedCondition"), Is.False,
				"A dock gate on the mobile MCV would reject every soldier, exactly as it does on the Centre.");

			Assert.That(int.Parse(fields["TotalSupply"]), Is.GreaterThan(0),
				"A zero pool would render an empty bar and serve nobody.");
		}
	}
}
