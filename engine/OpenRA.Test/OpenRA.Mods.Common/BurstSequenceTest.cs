#region Copyright & License Information
/*
 * Pins the interrupted-burst rules in Armament's burst counter. Pure-math test;
 * no Actor / World. Reported from playtest 260827: a Hind whose rocket burst was
 * broken off partway came back firing one or two rockets instead of a full pod,
 * because the stale-burst path decremented the counter instead of restoring it.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Test
{
	[TestFixture]
	public class BurstSequenceTest
	{
		// mods/ww3mod/rules/weapons/weapons-ballistics.yaml, `RocketPods` — the Hind's
		// main payload (ingame/aircraft-russia.yaml:210). The reported weapon.
		const int HindBurst = 10;
		const int HindWait = 5;
		static readonly int[] HindDelays = { 1 };

		// weapons-missiles.yaml, `TimerWolf_Missiles`. The 4-shot case, so every
		// interruption point can be enumerated.
		const int FourBurst = 4;
		const int FourWait = 30;
		static readonly int[] FourDelays = { 3 };

		// weapons-other.yaml, `Mandible` / `MandibleHeavy`. The inter-shot delay is
		// LONGER than the between-bursts wait on both, which is the shape that made a
		// gap-since-last-shot rule misfire in the middle of a healthy burst.
		const int MandibleBurst = 2;
		const int MandibleWait = 10;
		static readonly int[] MandibleDelays = { 14 };

		// weapons-ballistics.yaml, `30mm.Heli` — the only multi-entry BurstDelays in the mod.
		const int HeliBurst = 11;
		const int HeliWait = 10;
		static readonly int[] HeliDelays = { 3, 3, 3, 3, 6, 3, 3, 3, 3, 8 };

		// Drives the real BurstSequence calls in the order Armament makes them:
		// CheckFire consults the stale gate before firing, UpdateBurst advances after.
		sealed class BurstDriver
		{
			readonly int weaponBurst;
			readonly int[] delays;
			readonly int burstWait;

			public int Burst;
			public int StaleTick = BurstSequence.NoPendingBurst;
			public int Wait;
			public bool Completed;
			public int Tick;
			public bool ResetOnLastShot;

			public BurstDriver(int weaponBurst, int[] delays, int burstWait)
			{
				this.weaponBurst = weaponBurst;
				this.delays = delays;
				this.burstWait = burstWait;
				Burst = weaponBurst;
			}

			public void Fire()
			{
				// Armament.CheckFire: an interrupted burst restarts from full before the shot.
				ResetOnLastShot = BurstSequence.IsStale(StaleTick, Tick);
				if (ResetOnLastShot)
					Reset();

				// Armament.UpdateBurst.
				var step = BurstSequence.Advance(Burst, weaponBurst, delays, burstWait, Tick);
				Burst = step.Burst;
				StaleTick = step.StaleTick;
				Wait = step.Wait;
				Completed = step.Completed;

				if (step.Completed)
					Reset();
			}

			// Armament.ResetBurst. No BurstMultiplier is active here, so the full burst is
			// the raw weapon value.
			void Reset()
			{
				Burst = weaponBurst;
				StaleTick = BurstSequence.NoPendingBurst;
			}

			public void Idle(int ticks) { Tick += ticks; }

			// Fires until the burst completes, waiting exactly the scheduled delay between
			// shots, and reports how many shots the burst was worth.
			public int RunBurstToCompletion()
			{
				for (var shots = 1; shots <= 200; shots++)
				{
					Fire();
					if (Completed)
						return shots;

					Idle(Wait);
				}

				throw new InvalidOperationException("Burst never completed.");
			}
		}

		// Fires a partial burst and leaves the clock at the moment the next shot was due —
		// i.e. the scheduled inter-shot delay has just elapsed and nothing fired. From here
		// the reset is exactly one BurstWait away.
		static BurstDriver Interrupted(int weaponBurst, int[] delays, int burstWait, int shotsBeforeBreak)
		{
			var d = new BurstDriver(weaponBurst, delays, burstWait);
			for (var i = 0; i < shotsBeforeBreak; i++)
			{
				d.Fire();
				Assert.IsFalse(d.Completed, "Test set-up fired past the end of the burst.");
				d.Idle(d.Wait);
			}

			return d;
		}

		[Test]
		public void InterruptedHindRocketBurstComesBackFull()
		{
			// Three rockets away, then the Hind breaks off and comes round again.
			var d = Interrupted(HindBurst, HindDelays, HindWait, 3);
			var breakTick = d.Tick;
			d.Idle(HindWait);

			var shots = d.RunBurstToCompletion();

			Assert.AreEqual(HindBurst, shots,
				$"A rocket pod broken off after 3 of {HindBurst} rockets delivered {shots} rocket(s) on the " +
				$"next pass instead of a full pod. The stale-burst path used to decrement the counter rather " +
				$"than restore it, which is what left the Hind firing one or two rockets. " +
				$"(interrupted at tick {breakTick}, re-engaged at {d.Tick})");
		}

		[Test]
		public void EveryInterruptionPointComesBackFull()
		{
			// Enumerates the whole 4-shot burst: broken after 1, 2 and 3 shots.
			for (var broken = 1; broken < FourBurst; broken++)
			{
				var d = Interrupted(FourBurst, FourDelays, FourWait, broken);
				Assert.AreEqual(FourBurst - broken, d.Burst,
					$"Counter drifted before the interruption: {broken} of {FourBurst} shots fired.");

				d.Idle(FourWait);
				var shots = d.RunBurstToCompletion();

				Assert.AreEqual(FourBurst, shots,
					$"A {FourBurst}-shot burst interrupted after {broken} shot(s) delivered {shots} on the " +
					$"next engagement. Every interruption point must come back to a full burst.");
			}
		}

		[Test]
		public void ShortInterruptionsResumeTheBurst()
		{
			// Guard against over-correcting into "any gap at all restarts the burst": a unit that
			// is a tick or two late must finish the burst it started, or a 15-round Tunguska could
			// never get past its first round.
			var d = Interrupted(FourBurst, FourDelays, FourWait, 1);
			d.Idle(1);

			var shots = d.RunBurstToCompletion();

			Assert.AreEqual(FourBurst - 1, shots,
				$"A burst that stalled for a single tick restarted from full ({shots} shots). Only a gap " +
				$"longer than the scheduled delay plus a full BurstWait counts as an interruption.");
		}

		[Test]
		public void ResetThresholdIsTheScheduledDelayPlusOneBurstWait()
		{
			// The user's spec: the reset "should take as long as it takes between bursts". The clock
			// starts when the next shot fails to arrive, so from the moment a shot is overdue the
			// reset is exactly one BurstWait away. Pin both sides of that boundary.
			var justShort = Interrupted(FourBurst, FourDelays, FourWait, 2);
			justShort.Idle(FourWait - 1);
			Assert.AreEqual(FourBurst - 2, justShort.RunBurstToCompletion(),
				$"A shot overdue by {FourWait - 1} ticks restarted the burst. The reset is not due until a " +
				$"full BurstWait ({FourWait}) has passed with nothing fired.");

			var justPast = Interrupted(FourBurst, FourDelays, FourWait, 2);
			justPast.Idle(FourWait);
			Assert.AreEqual(FourBurst, justPast.RunBurstToCompletion(),
				$"A shot overdue by a full BurstWait ({FourWait} ticks) resumed the partial burst instead of " +
				"restarting it. The reset is due at exactly that tick.");
		}

		[Test]
		public void HealthyBurstIsNeverDeclaredStale()
		{
			// Mandible's inter-shot delay (14) exceeds its BurstWait (10). Keying the reset off the
			// raw gap since the last shot declared this burst stale between its two shots, so the
			// weapon fired single shots forever and never completed a burst.
			var weapons = new[]
			{
				("RocketPods", HindBurst, HindDelays, HindWait),
				("TimerWolf_Missiles", FourBurst, FourDelays, FourWait),
				("Mandible", MandibleBurst, MandibleDelays, MandibleWait),
				("30mm.Heli", HeliBurst, HeliDelays, HeliWait)
			};

			foreach (var (name, burst, delays, wait) in weapons)
			{
				var d = new BurstDriver(burst, delays, wait);
				for (var shot = 1; shot <= burst; shot++)
				{
					d.Fire();
					Assert.IsFalse(d.ResetOnLastShot,
						$"{name}: shot {shot} of {burst} was treated as the start of a new burst even though " +
						$"the weapon fired exactly on schedule (delay {delays[0]}, BurstWait {wait}).");

					if (shot < burst)
						Assert.IsFalse(d.Completed, $"{name}: burst ended early at shot {shot} of {burst}.");
					else
						Assert.IsTrue(d.Completed, $"{name}: burst did not complete after {burst} shots.");

					d.Idle(d.Wait);
				}
			}
		}

		[Test]
		public void InterruptingCannotRaiseTheFiringRateAtAnyBreakPoint()
		{
			// The main balance risk of restarting from full is that breaking off deliberately
			// becomes a way to skip the between-bursts wait. It cannot: a burst broken after k
			// shots still owes the delay for the shot that never came, so it pays k delays where
			// an uninterrupted burst of the same length pays k-1. Swept over every break point of
			// every multi-shot shape in the mod rather than argued once.
			var weapons = new[]
			{
				("RocketPods", HindBurst, HindDelays, HindWait),
				("TimerWolf_Missiles", FourBurst, FourDelays, FourWait),
				("Mandible", MandibleBurst, MandibleDelays, MandibleWait),
				("30mm.Heli", HeliBurst, HeliDelays, HeliWait),
				("30mm.Tunguska", 15, new[] { 1 }, 20),
				("GradRockets", 40, new[] { 4 }, 100)
			};

			foreach (var (name, burst, delays, wait) in weapons)
			{
				var straight = new BurstDriver(burst, delays, wait);
				var straightShots = straight.RunBurstToCompletion();
				var straightTicks = straight.Tick + straight.Wait;

				for (var k = 1; k <= burst; k++)
				{
					var broken = new BurstDriver(burst, delays, wait);
					for (var i = 0; i < k; i++)
					{
						broken.Fire();
						broken.Idle(broken.Wait);
					}

					// Idle out the rest of the reset, then the cycle repeats from full.
					broken.Idle(wait);

					// shots-per-tick compared by cross-multiplication to stay in integers.
					Assert.LessOrEqual(k * straightTicks, straightShots * broken.Tick,
						$"{name}: breaking off after {k} of {burst} shots delivered {k} shots per " +
						$"{broken.Tick} ticks, beating the {straightShots} per {straightTicks} of an " +
						"uninterrupted burst. A reset must never be cheaper than finishing the burst.");
				}
			}
		}

		[Test]
		public void SingleShotWeaponsAreUnaffected()
		{
			// Burst: 1 is 22 of the 24 burst declarations in the mod. Every shot completes its own
			// burst, so no partial burst is ever outstanding and the stale gate can never fire.
			var d = new BurstDriver(1, new[] { 1 }, 20);
			for (var i = 0; i < 5; i++)
			{
				d.Fire();
				Assert.IsTrue(d.Completed, "A Burst: 1 shot must complete its burst.");
				Assert.IsFalse(d.ResetOnLastShot, "A Burst: 1 weapon must never trip the interrupted-burst reset.");
				Assert.AreEqual(BurstSequence.NoPendingBurst, d.StaleTick,
					"A Burst: 1 weapon must never leave a partial burst outstanding.");
				d.Idle(1000);
			}
		}

		[Test]
		public void MultiEntryBurstDelaysAreConsumedInOrder()
		{
			// 30mm.Heli's list is uneven (a 6 and an 8 among the 3s); an off-by-one in the index
			// would silently reshape its cadence.
			var d = new BurstDriver(HeliBurst, HeliDelays, HeliWait);
			for (var i = 0; i < HeliDelays.Length; i++)
			{
				d.Fire();
				Assert.AreEqual(HeliDelays[i], d.Wait,
					$"Shot {i + 1} of {HeliBurst} scheduled a {d.Wait}-tick delay, expected {HeliDelays[i]}.");
				d.Idle(d.Wait);
			}
		}
	}
}
