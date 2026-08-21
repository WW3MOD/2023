#region Copyright & License Information
/*
 * WW3MOD dry-rearm leash — contract + anti-drift pin.
 *
 * USER RULING, 2026-08-21: the self-dispatch path that had no distance test at all
 * (AmmoPool.INotifyBecomingIdle -> AutoRearmIfAllEmpty, and the same method reached from firing the
 * last round) gets a 30-cell bound, "reusing a limit that already exists and was already thought
 * about, rather than inventing a new number".
 *
 * THE VALUE IS SHARED; THE FIELD IS NOT, and that split is the thing this file exists to keep honest.
 * The obvious implementation was to read AutoSeekSuppliesInfo.ReturnWhenEmptyLeashCells, which already
 * carries 30. It would have been wrong: AutoSeekSupplies is declared on ^Soldier alone, while
 * AutoRearmIfAllEmpty runs on every non-aircraft actor holding an AmmoPool — vehicles included. A
 * vehicle would have kept no leash at all, and the gap would have been invisible at the only site
 * anyone reads.
 *
 * So the bound lives on AmmoPoolInfo, where the behaviour lives, and the two are held together by:
 *   - the DEFAULTS being pinned equal here, so changing one without the other fails a test rather
 *     than silently drifting apart, which was the specific hazard raised;
 *   - the DISTANCE MATH being literally the same function (SupplyHuntMath.WithinCellBudget), so the
 *     metric, the boundary and the zero-semantics cannot diverge even if the numbers do.
 *
 * NOTE ON RED. The leash is new behaviour, so these tests are green by construction and prove only
 * that the contract is what it claims. They are NOT evidence that a distant unit stays put in a
 * running game — that needs a scenario, named in the report.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.BotModules;

namespace OpenRA.Test
{
	[TestFixture]
	public class DryRearmLeashTest
	{
		[Test]
		public void TheDryLeashDefaultMatchesTheBreakOffLeashItWasTakenFrom()
		{
			// The whole point of the ruling: reuse a number already reasoned about. If someone retunes
			// one of these, this failure is the prompt to decide whether the other should move too —
			// which is a decision, not a chore, because the two bound different things (self-dispatch
			// after running dry vs. interrupting an order the player gave).
			Assert.That(new AmmoPoolInfo().DryRearmLeashCells,
				Is.EqualTo(new AutoSeekSuppliesInfo().ReturnWhenEmptyLeashCells),
				"AmmoPoolInfo.DryRearmLeashCells has drifted away from " +
				"AutoSeekSuppliesInfo.ReturnWhenEmptyLeashCells. They were deliberately given the same " +
				"default (user ruling 2026-08-21: reuse the limit that already exists rather than invent " +
				"a second number). Separate fields are correct — AutoSeekSupplies is infantry-only while " +
				"the dry self-dispatch also runs on vehicles — but the defaults diverging silently is " +
				"exactly what that split was warned about.");
		}

		[Test]
		public void TheLeashUsesChessboardCellsAndIsInclusiveAtTheBoundary()
		{
			var leash = new AmmoPoolInfo().DryRearmLeashCells;

			// Same predicate the live dispatch calls, so this pins the real rule rather than a
			// re-derivation of it. Chessboard: a pure diagonal at the budget is IN, which Euclidean
			// distance would have rejected at ~42 cells.
			Assert.That(SupplyHuntMath.WithinCellBudget(leash, 0, leash), Is.True, "straight line at the budget must be admitted");
			Assert.That(SupplyHuntMath.WithinCellBudget(leash, leash, leash), Is.True, "a diagonal at the budget is 30 chessboard cells, not 42");
			Assert.That(SupplyHuntMath.WithinCellBudget(leash + 1, 0, leash), Is.False, "one cell past the budget must be refused");
			Assert.That(SupplyHuntMath.WithinCellBudget(-leash, -leash, leash), Is.True, "sign must not matter");
		}

		[Test]
		public void ANonPositiveLeashAdmitsNothing()
		{
			// Stated because the codebase carries TWO opposite zero-semantics for this one idea:
			// AutoSeekSupplies' budget treats 0 as "admit nothing", PoiOffensiveBotModule's
			// OutOfAmmoRearmSeekRadiusCells treats 0 as "unlimited". DryRearmLeashCells follows the
			// former. Getting this backwards turns "never self-dispatch" into "walk anywhere".
			Assert.That(SupplyHuntMath.WithinCellBudget(1, 0, 0), Is.False);
			Assert.That(SupplyHuntMath.WithinCellBudget(0, 0, 0), Is.False);
			Assert.That(SupplyHuntMath.WithinCellBudget(1, 0, -5), Is.False);
		}

		[Test]
		public void AnActorsLeashIsTheTightestOfItsPoolsAndDoesNotDependOnWhichPoolAsks()
		{
			// Several infantry carry primary + secondary pools, and INotifyBecomingIdle delivers to each
			// in turn — so a per-instance read would make the bound depend on trait ordering. Resolution
			// is across all pools, minimum wins. These call the SHIPPED method, not a copy of it: it
			// takes the raw values precisely so a test cannot end up agreeing with itself.
			Assert.That(AmmoPool.ResolveDryRearmLeash(new[] { 30, 30 }), Is.EqualTo(30));
			Assert.That(AmmoPool.ResolveDryRearmLeash(new[] { 30, 12 }), Is.EqualTo(12));
			Assert.That(AmmoPool.ResolveDryRearmLeash(new[] { 12, 30 }), Is.EqualTo(12), "order of the pools must not change the answer");
			Assert.That(AmmoPool.ResolveDryRearmLeash(new[] { 0, 30 }), Is.EqualTo(0), "a pool that admits nothing tightens the actor");

			// No pools cannot reach the caller (AllPoolsEmpty is false for an empty set); the fallback
			// is stated so it cannot accidentally become "unlimited".
			Assert.That(AmmoPool.ResolveDryRearmLeash(Enumerable.Empty<int>()), Is.EqualTo(0));
		}
	}
}
