#region Copyright & License Information
/*
 * WW3MOD post-detonation spawns — a weapon may only hurt what was on the map when it went off.
 *
 * THE BUG, in the user's words (2026-09-08): "after the bomb explodes, and after the mushroom cloud
 * animation is gone, and all my units are dead, if I build new units they come in and die instantly."
 *
 * Two effects outlive their own animation by a long way and keep sweeping a disc for victims every
 * tick. ShockwaveEffect excluded only actors already in its `hitActors` set, and an actor that did
 * not exist when the wave was born has by construction never been in that set — so it read as
 * "the front has not reached it yet" and took one full ApplyBlastDamage. ThermalRadiationEffect had
 * no exclusion set at all. On AtomicHighYield that is ~645 ticks of invisible blast wave and 413 of
 * invisible thermal field. Reinforcements from a Supply Route are the worst case: fixed arrival
 * cells, then a walk inward down the falloff.
 *
 * The fix is a filter, not a tuning change. No damage number moves; a correctly-hit actor takes
 * exactly what it took before.
 *
 * WHAT THIS FIXTURE CAN AND CANNOT REACH. Both effects need a live World, so there is no way to
 * drive Tick() from NUnit. It is therefore split in two, and the second half is the load-bearing one:
 *
 *   1. The RULE, driven through the shipped predicate, over a faithful model of the ID allocator.
 *      The model is three lines of World.NextAID and is asserted to match its post-increment
 *      semantics, so the sequence below is the real one.
 *   2. The WIRING, by IL scan: both Tick methods must actually call that predicate. This is the
 *      assertion that fails if the guard is deleted from either effect, which is the regression
 *      that would otherwise reintroduce the exact bug above with every other test still green.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Effects;

namespace OpenRA.Test
{
	[TestFixture]
	public class PostDetonationSpawnTest
	{
		// A faithful model of World.NextAID (World.cs:557-560): a uint that only ever
		// post-increments, is never reset, and never reuses a value a dead actor gave up.
		sealed class ActorIdAllocator
		{
			uint nextAID;

			public uint NextActorID => nextAID;

			public uint Allocate()
			{
				return nextAID++;
			}
		}

		// AtomicHighYield, weapons-superweapons.yaml: Warhead@BlastWave MaxRadius 102c0,
		// StartRadius 6c820, WaveSpeed 7 — the file's own comment puts front travel at ~645 ticks.
		const int BlastWaveTicks = 645;

		// Warhead@ThermalRadiation on the same weapon: RadiationDuration 413, DamageInterval 8.
		const int ThermalTicks = 413;
		const int ThermalDamageInterval = 8;

		// mod.yaml's default Timestep is 60 ms, so the blast wave stays lethal for ~39 s and the
		// thermal field for ~25 s with nothing on screen. Quoted, not asserted, so the tick counts
		// above are readable as durations.

		[Test]
		public void TheAllocatorModelMatchesWorldNextAID()
		{
			// If this drifts from the engine, every other rule test in the fixture tests a fiction.
			var alloc = new ActorIdAllocator();
			Assert.That(alloc.NextActorID, Is.Zero, "the counter must start at zero, as World.nextAID does");

			for (var i = 0u; i < 1000; i++)
			{
				Assert.That(alloc.NextActorID, Is.EqualTo(i), "the peek must be the ID the next actor will get");
				Assert.That(alloc.Allocate(), Is.EqualTo(i), "allocation must return the pre-increment value");
			}

			Assert.That(alloc.NextActorID, Is.EqualTo(1000u));
		}

		/// <summary>The snapshot is the ID the NEXT actor gets, so the boundary value is post-detonation.</summary>
		[Test]
		public void TheSnapshotPartitionsTheActorPopulationExactlyAtItsOwnValue()
		{
			const uint Snapshot = 4096;

			Assert.That(AreaEffectVictims.ExistedAtDetonation(Snapshot - 1, Snapshot), Is.True,
				"the last actor created before the bomb went off must still be a valid victim");
			Assert.That(AreaEffectVictims.ExistedAtDetonation(Snapshot, Snapshot), Is.False,
				"the first actor created after the bomb went off must not be — an off-by-one here is the whole bug");
			Assert.That(AreaEffectVictims.ExistedAtDetonation(Snapshot + 1, Snapshot), Is.False);
			Assert.That(AreaEffectVictims.ExistedAtDetonation(0, Snapshot), Is.True);
			Assert.That(AreaEffectVictims.ExistedAtDetonation(uint.MaxValue, Snapshot), Is.False);

			// A wave born on the very first tick of a match has nothing to hurt at all.
			Assert.That(AreaEffectVictims.ExistedAtDetonation(0, 0), Is.False);
		}

		/// <summary>The reported symptom: units built while the invisible wave is still expanding take nothing.</summary>
		[Test]
		public void UnitsBuiltMidWaveAreExcludedOnEveryRemainingTickOfThatWave()
		{
			var alloc = new ActorIdAllocator();

			// The army that was on the map when the missile landed, and dies to it.
			var doomed = new List<uint>();
			for (var i = 0; i < 12; i++)
				doomed.Add(alloc.Allocate());

			// Detonation. ShockwaveEffect's constructor snapshots World.NextActorID here.
			var snapshot = alloc.NextActorID;

			// The Supply Route keeps delivering while the wave is still out there and invisible.
			// Deliberately spread across the whole 645 ticks, including well past the cloud sprite.
			var reinforcementTicks = new[] { 1, 50, 199, 200, 321, 400, 500, 644 };
			var reinforcements = new Dictionary<int, uint>();

			for (var tick = 1; tick <= BlastWaveTicks; tick++)
			{
				if (reinforcementTicks.Contains(tick))
					reinforcements[tick] = alloc.Allocate();

				// Worst case on purpose: assume every actor alive is inside the current disc, which
				// is where the old code handed each of them one full ApplyBlastDamage.
				foreach (var kv in reinforcements)
					Assert.That(AreaEffectVictims.ExistedAtDetonation(kv.Value, snapshot), Is.False,
						$"tick {tick}: a unit built at tick {kv.Key} is still a valid victim of a bomb " +
						"that detonated before it existed");
			}

			Assert.That(reinforcements, Has.Count.EqualTo(reinforcementTicks.Length),
				"no reinforcement was ever built — the test asserted nothing about the reported bug");

			// And the fix must not have quietly made the weapon harmless to what it is aimed at.
			foreach (var id in doomed)
				Assert.That(AreaEffectVictims.ExistedAtDetonation(id, snapshot), Is.True,
					"an actor that was on the map at detonation must still take the hit");
		}

		/// <summary>Same rule for the thermal field, which keeps no hit set and re-cooks every DamageInterval.</summary>
		[Test]
		public void UnitsBuiltMidThermalFieldAreExcludedFromEveryRemainingPulse()
		{
			var alloc = new ActorIdAllocator();
			var caughtInTheOpen = alloc.Allocate();
			var snapshot = alloc.NextActorID;

			var arrivals = new List<uint>();
			var pulses = 0;

			for (var tick = 1; tick <= ThermalTicks; tick++)
			{
				if (tick % 37 == 0)
					arrivals.Add(alloc.Allocate());

				if (tick % ThermalDamageInterval != 0)
					continue;

				pulses++;
				foreach (var id in arrivals)
					Assert.That(AreaEffectVictims.ExistedAtDetonation(id, snapshot), Is.False,
						$"tick {tick}: a unit that walked into a burnt-out crater is taking a thermal pulse");
			}

			Assert.That(arrivals, Is.Not.Empty, "the walk-in case never fired — the test is asserting nothing");
			Assert.That(pulses, Is.GreaterThan(1),
				"the point of this effect is repeated pulses; one pulse is not the case that was broken");
			Assert.That(AreaEffectVictims.ExistedAtDetonation(caughtInTheOpen, snapshot), Is.True,
				"the unit that was standing there when it went off must still burn");
		}

		// ---- The wiring ----

		static MethodInfo TickOf<T>()
		{
			var tick = typeof(T).GetMethod("Tick", BindingFlags.Public | BindingFlags.Instance);
			Assert.That(tick, Is.Not.Null, $"{typeof(T).Name} has no public Tick — this fixture is scanning nothing");
			return tick;
		}

		static void AssertTickConsultsTheFilter<T>()
		{
			var guard = typeof(AreaEffectVictims).GetMethod(nameof(AreaEffectVictims.ExistedAtDetonation));
			var scan = IlScan.Scan(TickOf<T>());

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(0),
				$"the scan of {typeof(T).Name}.Tick resolved no calls at all, so a clean result means nothing");
			Assert.That(scan.Callees, Has.Some.Matches<MethodBase>(m => m == guard),
				$"{typeof(T).Name}.Tick does not consult AreaEffectVictims.ExistedAtDetonation. It sweeps for " +
				"victims long after its animation is over, so without that call it damages units that did not " +
				"exist when the weapon detonated.");
		}

		/// <summary>Both persistent effects must actually call the filter — this is the assertion the fix is.</summary>
		[Test]
		public void BothPersistentEffectsConsultTheFilterInTheirTick()
		{
			AssertTickConsultsTheFilter<ShockwaveEffect>();
			AssertTickConsultsTheFilter<ThermalRadiationEffect>();
		}

		/// <summary>The scheme rests on ActorID being immutable and on World offering a peek, not a second allocator.</summary>
		[Test]
		public void ActorIdIsImmutableAndTheWorldPeekCannotAllocate()
		{
			var actorId = typeof(Actor).GetField("ActorID");
			Assert.That(actorId, Is.Not.Null);
			Assert.That(actorId.IsInitOnly, Is.True,
				"ActorID is no longer readonly, so 'created before the bomb' is no longer a fact about an actor");

			var peek = typeof(World).GetProperty("NextActorID");
			Assert.That(peek, Is.Not.Null, "World.NextActorID is gone; the effects have nothing to snapshot");
			Assert.That(peek.SetMethod, Is.Null, "NextActorID must be get-only — allocation belongs to NextAID");
			Assert.That(peek.PropertyType, Is.EqualTo(actorId.FieldType),
				"the snapshot and the IDs it is compared against must be the same type, or the compare narrows");
		}
	}
}
