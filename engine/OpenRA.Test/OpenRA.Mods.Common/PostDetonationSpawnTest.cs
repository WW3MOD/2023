#region Copyright & License Information
/*
 * WW3MOD post-detonation spawns — a weapon may only hurt what was on the map when it went off.
 *
 * THE BUG, in the user's words (2026-09-08): "after the bomb explodes, and after the mushroom cloud
 * animation is gone, and all my units are dead, if I build new units they come in and die instantly."
 *
 * Two effects outlive their own animation once the yield is large enough, and keep sweeping a disc
 * for victims every tick. ShockwaveEffect excluded only actors already in its `hitActors` set, and an
 * actor that did not exist when the wave was born has by construction never been in that set — so it
 * read as "the front has not reached it yet" and took one full ApplyBlastDamage.
 * ThermalRadiationEffect had no exclusion set at all. Reinforcements from a Supply Route are the
 * worst case: fixed arrival cells, then a walk inward down the falloff.
 *
 * WHAT THIS FIXTURE PINS, AND WHAT IT DELIBERATELY DOES NOT HAVE TO. An adversarial review of the
 * first version of this fix named two one-token mutations that shipped the bug with every test in
 * this file green: inverting the call-site guard, and transposing the two `uint` arguments of a
 * static predicate. Both were then designed out rather than tested for, so this fixture is smaller
 * than the hazard it covers:
 *
 *   - Transposition is a compile error. DetonationStamp is its own readonly struct and the
 *     comparison is an instance method taking ONE argument. Pinned structurally below, because the
 *     property is "no two same-typed parameters exist to transpose", which a reader cannot check by
 *     eye once the API grows.
 *   - Call-site inversion has nowhere to live. Both Tick methods enumerate an already-filtered
 *     sequence and contain no branch on the stamp at all.
 *   - The unfiltered sweep is unreachable from a Tick, which is the assertion that actually matters:
 *     IlScan proving a filter is CALLED is weak (it is a linear byte walk over call tokens and says
 *     nothing about whether the result is used), but IlScan proving FindActorsOnCircle is ABSENT is
 *     strong, because reintroducing the bug requires calling it.
 *
 * That leaves exactly one comparison in the whole feature — DetonationStamp.Predates(uint) — and it
 * is pinned exhaustively at its boundary.
 *
 * The remaining boundary is unchanged and worth restating: both effects need a live World, so no
 * test here drives Tick(). The rule is exercised through the shipped predicate over a model of the
 * ID allocator that is itself asserted against World.NextAID's post-increment semantics; the wiring
 * is exercised by IL scan, once per effect.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common;
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
		// Its mushroom cloud is nuke_large at DurationScalePercent 198, i.e. 199 * 1.98 = ~394 ticks,
		// so ~251 ticks of that wave are invisible. The gap is a LARGE-YIELD property and not a
		// universal one: `Atomic`'s wave is ~100 ticks against a 199-tick cloud and never outlives it.
		const int BlastWaveTicks = 645;

		// Warhead@ThermalRadiation on the same weapon: RadiationDuration 413, DamageInterval 8.
		const int ThermalTicks = 413;
		const int ThermalDamageInterval = 8;

		static DetonationStamp Stamp(uint firstActorIDAfter)
		{
			return new DetonationStamp(firstActorIDAfter);
		}

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

		/// <summary>The stamp is the ID the NEXT actor gets, so the boundary value is post-detonation.</summary>
		[Test]
		public void TheStampPartitionsTheActorPopulationExactlyAtItsOwnValue()
		{
			var stamp = Stamp(4096);

			Assert.That(stamp.Predates(4095), Is.True,
				"the last actor created before the bomb went off must still be a valid victim");
			Assert.That(stamp.Predates(4096), Is.False,
				"the first actor created after the bomb went off must not be — an off-by-one here is the whole bug");
			Assert.That(stamp.Predates(4097), Is.False);
			Assert.That(stamp.Predates(0), Is.True);
			Assert.That(stamp.Predates(uint.MaxValue), Is.False);

			// A wave born on the very first tick of a match has nothing to hurt at all.
			Assert.That(Stamp(0).Predates(0), Is.False);

			// The whole neighbourhood of the boundary, so an off-by-one cannot hide in a gap between
			// the hand-picked values above.
			for (var id = 4000u; id < 4200u; id++)
				Assert.That(stamp.Predates(id), Is.EqualTo(id < 4096u), $"id {id}");
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

			// Detonation. ShockwaveEffect's constructor takes DetonationStamp.Now(world) here.
			var stamp = Stamp(alloc.NextActorID);

			// The Supply Route keeps delivering while the wave is still out there and invisible.
			// Deliberately spread across the whole 645 ticks, including well past the cloud sprite.
			var reinforcementTicks = new[] { 1, 50, 199, 200, 321, 394, 400, 500, 644 };
			var reinforcements = new Dictionary<int, uint>();

			for (var tick = 1; tick <= BlastWaveTicks; tick++)
			{
				if (reinforcementTicks.Contains(tick))
					reinforcements[tick] = alloc.Allocate();

				// Worst case on purpose: assume every actor alive is inside the current disc, which
				// is where the old code handed each of them one full ApplyBlastDamage.
				foreach (var kv in reinforcements)
					Assert.That(stamp.Predates(kv.Value), Is.False,
						$"tick {tick}: a unit built at tick {kv.Key} is still a valid victim of a bomb " +
						"that detonated before it existed");
			}

			Assert.That(reinforcements, Has.Count.EqualTo(reinforcementTicks.Length),
				"no reinforcement was ever built — the test asserted nothing about the reported bug");

			// And the fix must not have quietly made the weapon harmless to what it is aimed at.
			foreach (var id in doomed)
				Assert.That(stamp.Predates(id), Is.True,
					"an actor that was on the map at detonation must still take the hit");
		}

		/// <summary>Same rule for the thermal field, which keeps no hit set and re-cooks every DamageInterval.</summary>
		[Test]
		public void UnitsBuiltMidThermalFieldAreExcludedFromEveryRemainingPulse()
		{
			var alloc = new ActorIdAllocator();
			var caughtInTheOpen = alloc.Allocate();
			var stamp = Stamp(alloc.NextActorID);

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
					Assert.That(stamp.Predates(id), Is.False,
						$"tick {tick}: a unit that walked into a burnt-out crater is taking a thermal pulse");
			}

			Assert.That(arrivals, Is.Not.Empty, "the walk-in case never fired — the test is asserting nothing");
			Assert.That(pulses, Is.GreaterThan(1),
				"the point of this effect is repeated pulses; one pulse is not the case that was broken");
			Assert.That(stamp.Predates(caughtInTheOpen), Is.True,
				"the unit that was standing there when it went off must still burn");
		}

		// ---- The shapes that make the two named mutations unwritable ----

		/// <summary>No method in the feature takes two same-typed parameters, so no call can be transposed.</summary>
		[Test]
		public void NoMethodInTheFeatureHasTwoParametersThatCouldBeSwapped()
		{
			foreach (var type in new[] { typeof(DetonationStamp), typeof(AreaEffectVictims) })
			{
				foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
				{
					var types = m.GetParameters().Select(p => p.ParameterType).ToArray();
					Assert.That(types, Is.Unique,
						$"{type.Name}.{m.Name} has two parameters of the same type. Transposing them would " +
						"compile, and on this code path that silently reintroduces the post-detonation bug.");
				}
			}
		}

		/// <summary>The stamp must stay a readonly value type — that is what makes the compare an instance call.</summary>
		[Test]
		public void TheStampIsAReadonlyStruct()
		{
			var t = typeof(DetonationStamp);
			Assert.That(t.IsValueType, Is.True, "DetonationStamp became a class; a null stamp now damages nothing at all");

			foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
				Assert.That(f.IsInitOnly, Is.True, $"DetonationStamp.{f.Name} is mutable, so a stamp can be moved after the fact");

			var predates = t.GetMethod(nameof(DetonationStamp.Predates), new[] { typeof(uint) });
			Assert.That(predates, Is.Not.Null, "the single comparison in the feature is gone");
			Assert.That(predates.IsStatic, Is.False,
				"Predates became static, so the stamp is an argument again and can be passed in the wrong position");
		}

		/// <summary>The Actor overload must carry no comparison of its own — it delegates to the pinned one.</summary>
		[Test]
		public void TheActorOverloadDelegatesToTheOneComparison()
		{
			var byId = typeof(DetonationStamp).GetMethod(nameof(DetonationStamp.Predates), new[] { typeof(uint) });
			var byActor = typeof(DetonationStamp).GetMethod(nameof(DetonationStamp.Predates), new[] { typeof(Actor) });
			Assert.That(byActor, Is.Not.Null, "the method group handed to Where is gone");

			var scan = IlScan.Scan(byActor);
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(0), "the scan resolved nothing, so a clean result means nothing");
			Assert.That(scan.Callees, Has.Some.Matches<MethodBase>(m => m == byId),
				"Predates(Actor) no longer delegates to Predates(uint) — it has grown a comparison of its own, " +
				"which is a second place for the boundary to be written backwards");
		}

		// ---- The wiring, one test per effect ----

		static MethodInfo TickOf<T>()
		{
			var tick = typeof(T).GetMethod("Tick", BindingFlags.Public | BindingFlags.Instance);
			Assert.That(tick, Is.Not.Null, $"{typeof(T).Name} has no public Tick — this fixture is scanning nothing");
			return tick;
		}

		static void AssertTickCannotReachTheUnfilteredSweep<T>()
		{
			var filtered = typeof(AreaEffectVictims).GetMethod(nameof(AreaEffectVictims.PreDetonationOnCircle));
			var unfiltered = typeof(WorldExtensions).GetMethod(nameof(WorldExtensions.FindActorsOnCircle));
			Assert.That(filtered, Is.Not.Null);
			Assert.That(unfiltered, Is.Not.Null);

			var scan = IlScan.Scan(TickOf<T>());
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(0),
				$"the scan of {typeof(T).Name}.Tick resolved no calls at all, so a clean result means nothing");

			Assert.That(scan.Callees, Has.Some.Matches<MethodBase>(m => m == filtered),
				$"{typeof(T).Name}.Tick does not sweep through AreaEffectVictims.PreDetonationOnCircle.");

			// The load-bearing half. A call to the filter can be present and its result discarded;
			// a call to the RAW sweep cannot be present and harmless, because that sweep is the bug.
			Assert.That(scan.Callees, Has.None.Matches<MethodBase>(m => m == unfiltered),
				$"{typeof(T).Name}.Tick calls WorldExtensions.FindActorsOnCircle directly. This effect keeps " +
				"sweeping for hundreds of ticks after its animation is over, so an unfiltered sweep damages " +
				"units that did not exist when the weapon detonated. Sweep through PreDetonationOnCircle.");
		}

		[Test]
		public void ShockwaveEffectTickCannotReachTheUnfilteredSweep()
		{
			AssertTickCannotReachTheUnfilteredSweep<ShockwaveEffect>();
		}

		[Test]
		public void ThermalRadiationEffectTickCannotReachTheUnfilteredSweep()
		{
			AssertTickCannotReachTheUnfilteredSweep<ThermalRadiationEffect>();
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
			Assert.That(peek, Is.Not.Null, "World.NextActorID is gone; the effects have nothing to stamp");
			Assert.That(peek.SetMethod, Is.Null, "NextActorID must be get-only — allocation belongs to NextAID");
			Assert.That(peek.PropertyType, Is.EqualTo(actorId.FieldType),
				"the stamp and the IDs it is compared against must be the same type, or the compare narrows");
		}
	}
}
