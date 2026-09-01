#region Copyright & License Information
/*
 * WW3MOD group-scatter waypoint honesty (2026-09-01).
 *
 * WHAT SHIFT-G PROMISES. GroupScatterHotkeyLogic redistributes "the MAIN points the player clicked"
 * (its own words, GroupScatterHotkeyLogic.cs:236). For an attack-move it reads that point from
 * AttackMoveActivity.OriginalDestination, whose declared purpose is to be the point, cached at
 * construction "for reliable group scatter extraction".
 *
 * WHAT WENT WRONG. AttackMove.ResolveOrder builds the activity from a closure that relocates the
 * click through Mobile.NearestMoveableCell, and the activity INFERRED OriginalDestination by running
 * that closure once and reading the resulting Move's destination. NearestMoveableCell is per-unit by
 * construction — it short-circuits on the unit's OWN location, tests CanEnterCell / CanStayInCell
 * against the unit's OWN locomotor, and gates on CanReach, which is the unit's OWN pathfinding
 * domain (Mobile.cs:850-871). One click by a mixed selection therefore recorded a DIFFERENT cell per
 * unit, and Shift-G replayed cells nobody clicked.
 *
 * THE ASYMMETRY THAT MAKES THIS A DEFECT RATHER THAN A CHOICE. Plain Move does not have this
 * problem, and the difference is one argument. Mobile.ResolveOrder passes the RAW cell with
 * evaluateNearestMovableCell: true (Mobile.cs:1092), so relocation happens later in Move.OnFirstRun
 * and Move.Destination at construction — which is what SmartMoveActivity.OriginalDestination
 * captures — is still the click. AttackMove passed an ALREADY-relocated cell. So Shift-G was already
 * honest for Move and dishonest for AttackMove, on the same screen, for the same click.
 *
 * WHY THESE TESTS ARE STRUCTURAL. The value is recorded on an Activity built from a live Actor with
 * a live Mobile inside a World, so there is no seam that lets NUnit observe two units disagreeing.
 * The autotest suite cannot reach it either: the harm is a divergence ACROSS a selection, and the
 * Test.GroupScatter binding exercises the spread but the assertion would have to be on an internal
 * activity field. So these pin the two structural facts that together forbid the defect —
 * (1) the player order site STATES the clicked cell rather than letting it be inferred, and
 * (2) relocation is still deferred into the closure, which is the separate property 1d239d60 fixed
 * and which the obvious "just hoist it back out" tidy-up would undo.
 * Said plainly so nobody reads a green run here as behavioural cover.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Widgets.Logic.Ingame;

namespace OpenRA.Test
{
	[TestFixture]
	public class GroupScatterWaypointTest
	{
		static MethodBase ResolveOrderOfAttackMove()
		{
			// AttackMove and AttackMoveInfo are internal to Mods.Common, so the type is fetched by name
			// rather than referenced. A rename must fail here loudly rather than skip the scan.
			var attackMove = typeof(AttackMoveActivity).Assembly.GetType("OpenRA.Mods.Common.Traits.AttackMove");
			Assert.That(attackMove, Is.Not.Null,
				"OpenRA.Mods.Common.Traits.AttackMove not found — this fixture is no longer scanning the " +
				"player attack-move order site it claims to.");

			var resolveOrder = attackMove.GetMethod("ResolveOrder",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
			Assert.That(resolveOrder, Is.Not.Null, "AttackMove.ResolveOrder not found.");

			return resolveOrder;
		}

		// THE PIN. The cell Shift-G replays has to be a property of the CLICK, so it must be handed in
		// by the site that knows the click. Inferring it from the move makes it a property of the UNIT.
		[Test]
		public void PlayerAttackMoveStatesTheClickedCellInsteadOfInferringIt()
		{
			var scan = IlScan.Scan(ResolveOrderOfAttackMove());

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(5),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in AttackMove.ResolveOrder — the " +
				"scanner is broken, not the code clean.");

			var constructed = scan.Callees
				.OfType<ConstructorInfo>()
				.Where(c => c.DeclaringType == typeof(AttackMoveActivity))
				.ToList();

			Assert.That(constructed, Is.Not.Empty,
				"AttackMove.ResolveOrder no longer constructs an AttackMoveActivity — this fixture is " +
				"pinning a code path that has moved.");

			foreach (var ctor in constructed)
			{
				var statesTheOrderPoint = ctor.GetParameters().Any(p => p.ParameterType == typeof(CPos));
				Assert.That(statesTheOrderPoint, Is.True,
					"AttackMove.ResolveOrder builds AttackMoveActivity through the INFERRING constructor " +
					$"({Describe(ctor)}), which derives OriginalDestination by running the move closure and " +
					"reading the resulting Move.Destination. That closure applies Mobile.NearestMoveableCell, " +
					"which answers per-unit (own location, own locomotor, own reachability domain), so one " +
					"click by a selection records a different cell for each unit and Shift-G replays cells " +
					"the player never clicked. Pass the clamped order cell explicitly instead.");
			}
		}

		// GUARDS 1d239d60, which this fix has to compose with rather than undo. Relocation belongs
		// INSIDE the closure so a shift-queued order resolves it when the move starts. Hoisting it back
		// to a local in ResolveOrder — the tidy-up that looks like it removes a duplicate call — restores
		// the stale-destination bug that test-queued-attackmove-stale-cell pins.
		[Test]
		public void ResolveOrderStillDefersRelocationIntoTheClosure()
		{
			var scan = IlScan.Scan(ResolveOrderOfAttackMove());

			var hoisted = scan.Callees.Any(c => c.Name == "NearestMoveableCell");

			Assert.That(hoisted, Is.False,
				"Mobile.NearestMoveableCell is called directly in AttackMove.ResolveOrder's own body rather " +
				"than inside the move closure. ResolveOrder runs the moment an order arrives, including a " +
				"shift-queued one, so relocating here answers 'what ground is reachable' at click time and " +
				"acts on it later. See MoveOrderTerms and test-queued-attackmove-stale-cell.");
		}

		// WHY THE DIVERGENCE MATTERS, stated against the real consumer rather than argued in prose.
		// This is a CONSEQUENCE demonstration, not a regression pin: CommonSuffixLength is unchanged by
		// the fix and both cases below pass before and after it. What the fix changes is which of the two
		// inputs a single click produces.
		[Test]
		public void DivergentRecordedCellsDestroyTheSharedSuffixThatPrefixPreservationNeedsOn()
		{
			var clicked = new CPos(30, 16);
			var relocatedForSecondUnit = new CPos(29, 16);

			var honest = Chains(
				new[] { (clicked, "AttackMove") },
				new[] { (clicked, "AttackMove") });

			var corrupted = Chains(
				new[] { (clicked, "AttackMove") },
				new[] { (relocatedForSecondUnit, "AttackMove") });

			Assert.Multiple(() =>
			{
				Assert.That(GroupScatterHotkeyLogic.CommonSuffixLength(honest), Is.EqualTo(1),
					"one click recorded identically by both units is one shared waypoint, which is what lets " +
					"Shift-G tell a group order apart from a per-unit prefix");

				Assert.That(GroupScatterHotkeyLogic.CommonSuffixLength(corrupted), Is.EqualTo(0),
					"a single click recorded as two different cells has NO shared suffix, so Shift-G falls " +
					"through to the legacy global pool, dedupes the two cells as two distinct waypoints, and " +
					"splits the selection between the clicked cell and one the player never clicked");
			});
		}

		static IReadOnlyList<IReadOnlyList<(CPos Cell, string OrderType)>> Chains(
			params (CPos, string)[][] chains)
		{
			return chains
				.Select(c => (IReadOnlyList<(CPos, string)>)c.ToList())
				.ToList();
		}

		static string Describe(ConstructorInfo ctor)
		{
			return "AttackMoveActivity(" + string.Join(", ", ctor.GetParameters()
				.Select(p => p.ParameterType.Name)) + ")";
		}
	}
}
