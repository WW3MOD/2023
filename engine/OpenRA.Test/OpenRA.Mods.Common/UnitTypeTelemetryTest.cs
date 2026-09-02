#region Copyright & License Information
/*
 * WW3MOD — composition-telemetry aggregation pins.
 *
 * UnitTypeTelemetry is the OBSERVER-ONLY per-actor-type tally that feeds the autotest/tournament
 * verdict's `unit_types` block (produced / lost / alive counts + costs). These tests drive the pure
 * counter primitives through realistic lifecycle sequences fed by UpdatesPlayerStatistics
 * (Created -> Produced, Killed -> Lost + RemoveAlive, Disposing/OwnerChanged -> alive moves) and pin
 * the resulting arithmetic, so the reporting schema can't silently drift. No world is mounted; this is
 * integer bookkeeping with zero simulation coupling.
 */
#endregion

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class UnitTypeTelemetryTest
	{
		[Test]
		public void Produced_AccumulatesCountCostAndAlive()
		{
			var t = new UnitTypeTelemetry();
			t.Produced("ranger", 500);
			t.Produced("ranger", 500);
			t.Produced("ranger", 500);

			var r = t["ranger"];
			Assert.That(r.ProducedCount, Is.EqualTo(3));
			Assert.That(r.ProducedCost, Is.EqualTo(1500));
			Assert.That(r.AliveCount, Is.EqualTo(3), "each production is alive until removed");
			Assert.That(r.AliveValue, Is.EqualTo(1500));
			Assert.That(r.LostCount, Is.EqualTo(0));
			Assert.That(r.LostCost, Is.EqualTo(0));
		}

		[Test]
		public void Lost_CountsLossButAliveIsRemovedSeparately()
		{
			// Mirrors UpdatesPlayerStatistics.Killed: Lost() records the loss, RemoveAlive() drops it live.
			var t = new UnitTypeTelemetry();
			t.Produced("tank", 1000);
			t.Produced("tank", 1000);
			t.Lost("tank", 1000);
			t.RemoveAlive("tank", 1000);

			var tank = t["tank"];
			Assert.That(tank.ProducedCount, Is.EqualTo(2));
			Assert.That(tank.ProducedCost, Is.EqualTo(2000));
			Assert.That(tank.LostCount, Is.EqualTo(1));
			Assert.That(tank.LostCost, Is.EqualTo(1000));
			Assert.That(tank.AliveCount, Is.EqualTo(1), "produced 2, one killed => one alive");
			Assert.That(tank.AliveValue, Is.EqualTo(1000));
		}

		[Test]
		public void AliveInvariant_ProducedMinusRemovedAcrossMixedFates()
		{
			// One type: produced 5, two killed (Lost+RemoveAlive), one disposed (RemoveAlive only),
			// leaving two alive. Produced stays monotonic; lost counts only combat deaths.
			var t = new UnitTypeTelemetry();
			for (var i = 0; i < 5; i++)
				t.Produced("apc", 400);

			t.Lost("apc", 400);
			t.RemoveAlive("apc", 400);
			t.Lost("apc", 400);
			t.RemoveAlive("apc", 400);

			t.RemoveAlive("apc", 400); // non-combat dispose: alive drops, not a "loss"

			var apc = t["apc"];
			Assert.That(apc.ProducedCount, Is.EqualTo(5));
			Assert.That(apc.LostCount, Is.EqualTo(2));
			Assert.That(apc.LostCost, Is.EqualTo(800));
			Assert.That(apc.AliveCount, Is.EqualTo(2), "5 produced - 2 killed - 1 disposed");
			Assert.That(apc.AliveValue, Is.EqualTo(800));
		}

		[Test]
		public void OwnerTransfer_MovesAliveBetweenTelemetriesWithoutFabricatingProduction()
		{
			// Simulates a capture: old owner loses the live unit (RemoveAlive, no Lost), new owner
			// gains it live (AddAlive) but is NOT credited with producing it.
			var oldOwner = new UnitTypeTelemetry();
			var newOwner = new UnitTypeTelemetry();

			oldOwner.Produced("engi", 200);
			oldOwner.RemoveAlive("engi", 200);
			newOwner.AddAlive("engi", 200);

			Assert.That(oldOwner["engi"].ProducedCount, Is.EqualTo(1));
			Assert.That(oldOwner["engi"].AliveCount, Is.EqualTo(0));
			Assert.That(oldOwner["engi"].LostCount, Is.EqualTo(0), "a transfer is not a combat loss");

			Assert.That(newOwner["engi"].ProducedCount, Is.EqualTo(0), "captured unit is not 'produced'");
			Assert.That(newOwner["engi"].AliveCount, Is.EqualTo(1));
			Assert.That(newOwner["engi"].AliveValue, Is.EqualTo(200));
		}

		[Test]
		public void Killed_CreditsTheKillerTypeWithoutTouchingItsOwnLifecycle()
		{
			// The credit side of the ledger, added for TradeEfficiencyMath: Killed() records what a type
			// DESTROYED. It must not disturb produced/alive/lost, which describe what happened TO the type —
			// the trade ratio is meaningless if a kill also reads as a production or a loss.
			var t = new UnitTypeTelemetry();
			t.Produced("tank", 1000);
			t.Killed("tank", 700);
			t.Killed("tank", 300);

			var tank = t["tank"];
			Assert.That(tank.KilledCount, Is.EqualTo(2));
			Assert.That(tank.KilledCost, Is.EqualTo(1000));
			Assert.That(tank.ProducedCount, Is.EqualTo(1), "a kill is not a production");
			Assert.That(tank.AliveCount, Is.EqualTo(1), "a kill does not change how many of ours are alive");
			Assert.That(tank.LostCount, Is.EqualTo(0), "a kill is not a loss");
		}

		[Test]
		public void KilledAndLost_AreIndependentAxesOfTheSameType()
		{
			// A type can be simultaneously productive and expensive; the trade ratio divides one by the
			// other, so they must accumulate separately rather than netting off.
			var t = new UnitTypeTelemetry();
			t.Produced("apc", 400);
			t.Killed("apc", 2500);
			t.Lost("apc", 400);
			t.RemoveAlive("apc", 400);

			var apc = t["apc"];
			Assert.That(apc.KilledCost, Is.EqualTo(2500));
			Assert.That(apc.LostCost, Is.EqualTo(400));
			Assert.That(apc.AliveCount, Is.EqualTo(0));
		}

		[Test]
		public void Sorted_IsDeterministicOrdinalByActorName()
		{
			var t = new UnitTypeTelemetry();
			t.Produced("ranger", 500);
			t.Produced("apc", 400);
			t.Produced("tank", 1000);

			var keys = t.Sorted().Select(kv => kv.Key).ToArray();
			Assert.That(keys, Is.EqualTo(new[] { "apc", "ranger", "tank" }));
			Assert.That(t.TypeCount, Is.EqualTo(3));
		}

		[Test]
		public void EmptyTelemetry_EnumeratesToNothing()
		{
			var t = new UnitTypeTelemetry();
			Assert.That(t.TypeCount, Is.EqualTo(0));
			Assert.That(t.Sorted().Any(), Is.False);
		}

		[Test]
		public void DistinctTypes_TrackedIndependently()
		{
			var t = new UnitTypeTelemetry();
			t.Produced("ranger", 500);
			t.Produced("tank", 1000);
			t.Lost("ranger", 500);
			t.RemoveAlive("ranger", 500);

			Assert.That(t["ranger"].AliveCount, Is.EqualTo(0));
			Assert.That(t["tank"].AliveCount, Is.EqualTo(1));
			Assert.That(t["tank"].LostCount, Is.EqualTo(0));
		}
	}
}
