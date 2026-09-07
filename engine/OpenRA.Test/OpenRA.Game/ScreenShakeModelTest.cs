#region Copyright & License Information
/*
 * WW3MOD screen-shake model fixture (2026-09-06).
 *
 * Screen shake is close to invisible in a still screenshot and cannot be judged from one, so this
 * fixture is the instrument the model is reviewed through: it asserts the properties the rework was
 * commissioned for, and TabulateShippedProfiles prints an amplitude-over-time trace of the shipped
 * call sites. Everything here samples ScreenShakeModel itself rather than a reimplementation of it,
 * so the table cannot drift away from what the game does.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ScreenShakeModelTest
	{
		const int Cell = 1024;

		// Unit(h) is h / 2^32, and the detune is f * (1 + jitter * (2 * Unit(h) - 1)). So a hash of
		// 2^31 maps to Unit 0.5 and a detune factor of exactly 1 -- the CENTRE of the band. Passing
		// 0 (the obvious choice) silently reports the bottom of it, which is 6% low and does not
		// look wrong.
		const uint JitterCentre = 2147483648u;

		static ScreenShakeModel Model()
		{
			return new ScreenShakeModel(new ScreenShakerInfo());
		}

		static ShakeEffect Effect(ShakeParams p, int spawn = 0, WPos? pos = null)
		{
			return new ShakeEffect
			{
				SpawnTime = spawn,
				Position = pos ?? new WPos(64 * Cell, 64 * Cell, 0),
				Params = p
			};
		}

		static ShakeParams Params(int duration, int intensity, int halfLife = 0, int attack = 2,
			int release = 0, int freqScale = 100)
		{
			return new ShakeParams
			{
				Duration = duration,
				Intensity = intensity,
				Multiplier = new float2(1, 1),
				AttackTicks = attack,
				DecayHalfLife = halfLife,
				ReleaseTicks = release,
				FrequencyScale = freqScale
			};
		}

		/// <summary>Normalised waveform with the envelope and falloff divided back out.</summary>
		static float[] Waveform(ScreenShakeModel m, ShakeEffect e, int distance, int samples)
		{
			var w = new float[samples];
			for (var t = 0; t < samples; t++)
			{
				var amp = m.Amplitude(e, t, distance);
				var s = m.Sample(e, e.SpawnTime + m.ArrivalDelay(e.Params, distance) + t, distance);
				w[t] = amp > 0.0001f ? s.X / amp : 0f;
			}

			return w;
		}

		/// <summary>Peak normalised autocorrelation over every lag in [minLag, maxLag].</summary>
		static float PeakAutocorrelation(float[] w, int minLag, int maxLag)
		{
			var energy = w.Sum(v => v * v);
			if (energy <= 0f)
				return 0f;

			var peak = 0f;
			for (var lag = minLag; lag <= maxLag && lag < w.Length; lag++)
			{
				var dot = 0f;
				var selfA = 0f;
				var selfB = 0f;
				for (var t = 0; t + lag < w.Length; t++)
				{
					dot += w[t] * w[t + lag];
					selfA += w[t] * w[t];
					selfB += w[t + lag] * w[t + lag];
				}

				var norm = (float)Math.Sqrt(selfA * selfB);
				if (norm > 0f)
					peak = Math.Max(peak, Math.Abs(dot / norm));
			}

			return peak;
		}

		[TestCase(TestName = "Shake waveform does not repeat, where the two-sine model it replaced repeats exactly.")]
		public void WaveformIsNotPeriodic()
		{
			var m = Model();
			var e = Effect(Params(220, 12));
			var w = Waveform(m, e, 0, 220);

			// Upstream was sin(2*pi*t/4): period exactly 4 ticks, so it self-matches perfectly at
			// lag 4 and every multiple of it. That is the buzz, stated numerically.
			var old = new float[220];
			for (var t = 0; t < old.Length; t++)
				old[t] = (float)Math.Sin(2 * Math.PI * t / 4);

			// Lags are capped at half the window so every correlation is computed over a decent
			// overlap; near the end of the array a handful of samples can correlate at almost
			// anything and the number stops meaning "repeats".
			var oldPeak = PeakAutocorrelation(old, 1, old.Length / 2);
			var newPeak = PeakAutocorrelation(w, 1, w.Length / 2);

			Assert.That(oldPeak, Is.GreaterThan(0.99f),
				"Control: the model this replaced repeats EXACTLY every 4 ticks and should self-match " +
				"essentially perfectly there.");
			Assert.That(newPeak, Is.LessThan(0.95f),
				$"Waveform repeats at some lag (peak autocorrelation {newPeak:F3}). The frequency " +
				"ratios have probably acquired a small-integer relation — they must stay mutually " +
				"irrational or the sum has a true period and reads as a buzz again.");

			// Not repeating is necessary but not sufficient: a waveform can be aperiodic and still be
			// dominated by one tone. Assert real spread of energy across the comb, since that is what
			// "broadband" actually means and what stops the motion reading as a single pitch.
			var info = new ScreenShakerInfo();
			var power = info.FrequencyWeights.Select(x => x * x).ToArray();
			var offFundamental = power.Skip(1).Sum() / power.Sum();
			Assert.That(offFundamental, Is.GreaterThan(0.35f),
				$"Only {offFundamental:P0} of the waveform energy sits outside the fundamental. " +
				"FrequencyWeights has been made too steep and the comb has collapsed toward one tone.");
		}

		[TestCase(TestName = "Bigger events shake at a lower frequency than small ones.")]
		public void FrequencyFallsWithEventSize()
		{
			var m = Model();

			// Hash is taken at a fixed position so jitter does not decide the comparison.
			var shell = m.Fundamental(Params(30, 2), 0, JitterCentre);
			var building = m.Fundamental(Params(45, 6), 0, JitterCentre);
			var tacNuke = m.Fundamental(Params(150, 13), 0, JitterCentre);
			var strategic = m.Fundamental(Params(260, 18), 0, JitterCentre);

			Assert.That(shell, Is.GreaterThan(building));
			Assert.That(building, Is.GreaterThan(tacNuke));
			Assert.That(tacNuke, Is.GreaterThan(strategic));

			// The spread has to be big enough to hear. Anything under ~2x and a nuke is just a
			// louder shell rather than a different kind of event.
			Assert.That(shell / strategic, Is.GreaterThan(2f),
				"Small and strategic events have collapsed onto nearly the same frequency — check " +
				"MaxFrequency and FrequencyIntensityExponent, the cap eats the lever if it bites.");

			// Nothing may cross Nyquist, or the top component folds back to a lower apparent one.
			foreach (var f in new[] { shell, building, tacNuke, strategic })
				foreach (var ratio in new ScreenShakerInfo().FrequencyRatios)
					Assert.That(f * ratio, Is.LessThanOrEqualTo(0.5f));
		}

		[TestCase(TestName = "Distance falloff is far gentler than the inverse square it replaced.")]
		public void FalloffIsGentlerThanInverseSquare()
		{
			var m = Model();
			var probe = Params(60, 8);

			var near = m.Falloff(probe, 10 * Cell);
			var far = m.Falloff(probe, 20 * Cell);

			// Inverse square would put the far sample at a quarter of the near one.
			Assert.That(far / near, Is.GreaterThan(0.25f * 1.5f),
				"Doubling the distance should not cost anywhere near the 4x that inverse square did.");

			// It must still be monotone, and it must still reach effectively nothing eventually.
			Assert.That(m.Falloff(probe, 0), Is.EqualTo(1f).Within(0.001f));
			Assert.That(m.Falloff(probe, 40 * Cell), Is.LessThan(m.Falloff(probe, 20 * Cell)));
			Assert.That(m.Falloff(probe, 150 * Cell), Is.LessThan(0.02f));
		}

		[TestCase(TestName = "The ground wave arrives well before the air blast that follows it.")]
		public void GroundWaveOutrunsTheBlastWave()
		{
			var m = Model();
			var probe = Params(60, 8);

			Assert.That(m.ArrivalDelay(probe, 0), Is.EqualTo(0));

			// BlastWave in weapons-superweapons.yaml uses WaveSpeed 7 ticks/cell for the air shock.
			// Ground-borne energy must be substantially quicker or the two land together and the
			// separation that makes a distant blast feel distant is lost.
			const int BlastWaveTicksPerCell = 7;
			foreach (var cells in new[] { 10, 30, 60, 100 })
			{
				var ground = m.ArrivalDelay(probe, cells * Cell);
				Assert.That(ground, Is.LessThan(cells * BlastWaveTicksPerCell / 4),
					$"At {cells} cells the ground wave should be far ahead of the shockwave.");
				Assert.That(ground, Is.GreaterThan(0));
			}

			Assert.That(m.ArrivalDelay(probe, 60 * Cell), Is.GreaterThan(m.ArrivalDelay(probe, 30 * Cell)));

			// And it is capped, so a huge map cannot leave an effect pending forever.
			Assert.That(m.ArrivalDelay(probe, 100000 * Cell),
				Is.EqualTo(new ScreenShakerInfo().MaxPropagationDelay));
		}

		[TestCase(TestName = "A stage can opt into air-blast speed and then tracks the visible shockwave.")]
		public void PerEffectPropagationTracksTheBlastWave()
		{
			var m = Model();

			var ground = Params(60, 8);
			var airBlast = Params(60, 8);

			// 7.8 t/cell is the air-blast figure derived on ScreenShakerInfo.PropagationTicksPerCell,
			// and BlastWave's own WaveSpeed of 7 is the same physics rounded to an int.
			airBlast.PropagationTicksPerCell = 7.8f;

			foreach (var cells in new[] { 10, 20, 30 })
			{
				var g = m.ArrivalDelay(ground, cells * Cell);
				var a = m.ArrivalDelay(airBlast, cells * Cell);

				Assert.That(a, Is.GreaterThan(g),
					$"At {cells} cells the air-blast stage must land after the ground wave.");

				// Within a tick or two of where BlastWave's own wavefront is at that range.
				Assert.That(a, Is.EqualTo(cells * 7).Within(cells),
					$"At {cells} cells the air-blast stage should arrive with the visible wavefront.");
			}

			// And at ground zero there is nothing to separate — everything is simultaneous.
			Assert.That(m.ArrivalDelay(airBlast, 0), Is.EqualTo(0));
		}

		[TestCase(TestName = "A slow stage can raise its own arrival ceiling and then tracks its front to the edge.")]
		public void PerEffectPropagationCapCarriesTheWaveToTheEdge()
		{
			var m = Model();
			var info = new ScreenShakerInfo();

			// AtomicHighYield Warhead@Shake5 as shipped: the air blast, 6.3 ticks/cell across a
			// 102-cell blast radius, so its front is 643 ticks wide.
			var air = Params(120, 7, halfLife: 32, freqScale: 60);
			air.PropagationTicksPerCell = 6.3f;

			// Where the GLOBAL ceiling saturates this stage. Stated as arithmetic rather than as a
			// remembered number, because the mean front speed has already been retuned once and the
			// 38.5 cells an earlier note quoted was measured at the 7.8 t/cell this no longer uses.
			var globalBiteCells = info.MaxPropagationDelay / 6.3f;
			Assert.That(globalBiteCells, Is.LessThan(60f),
				$"The global ceiling only reaches {globalBiteCells:F1} cells at this speed, which is " +
				"why the stage needs its own — if this stopped being true the case is moot.");

			var capped = air;
			capped.MaxPropagationDelay = 645;

			foreach (var cells in new[] { 60, 80, 102 })
			{
				var withGlobal = m.ArrivalDelay(air, cells * Cell);
				var withOwn = m.ArrivalDelay(capped, cells * Cell);

				Assert.That(withGlobal, Is.EqualTo(info.MaxPropagationDelay),
					$"Control: at {cells} cells the un-capped stage is expected to be saturated.");

				// The whole defect in one assertion: with only the global ceiling every camera past
				// 47 cells is told the rattle arrives at the same instant, however far out it is.
				Assert.That(withOwn, Is.GreaterThan(withGlobal),
					$"At {cells} cells the shake still arrives at the global {info.MaxPropagationDelay}-tick " +
					"ceiling, so the far half of this weapon's blast radius gets it at a flat 18 s " +
					"while the visible wavefront is still seconds away.");

				Assert.That(withOwn, Is.EqualTo((int)(cells * 6.3f)).Within(2),
					$"At {cells} cells the stage should land with its own front.");
			}

			// It is still bounded — at the event's own number rather than at nothing.
			Assert.That(m.ArrivalDelay(capped, 100000 * Cell), Is.EqualTo(645));

			// And an event that sets nothing is untouched, which is what keeps this a per-event
			// lever rather than a global retune of every shake in the mod.
			Assert.That(m.MaxDelay(Params(60, 8)), Is.EqualTo(info.MaxPropagationDelay));
			Assert.That(m.ArrivalDelay(Params(60, 8), 100000 * Cell), Is.EqualTo(info.MaxPropagationDelay));
		}

		/// <summary>Locates a file under mods/ww3mod by walking up from the test binary.</summary>
		static string FindMod(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod" }.Concat(relative).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/" + string.Join("/", relative));
		}

		static string Field(MiniYamlNode node, string key)
		{
			return node.Value.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value?.Trim();
		}

		/// <summary>
		/// Every weapons file the mod actually loads, read out of mod.yaml rather than listed here.
		/// A fixture that names its own inputs stays green while the thing it claims to cover grows
		/// past it, so the population is taken from the same manifest the game reads.
		/// </summary>
		static IEnumerable<string> WeaponFiles()
		{
			var manifest = MiniYaml.FromFile(FindMod("mod.yaml"));
			var weapons = manifest.FirstOrDefault(n => n.Key == "Weapons");
			Assert.That(weapons, Is.Not.Null, "mod.yaml has no Weapons: section — this walk is reading nothing.");

			foreach (var entry in weapons.Value.Nodes)
			{
				var path = entry.Key.Split('|').Last().Split('/');
				yield return FindMod(path);
			}
		}

		[TestCase(TestName = "Every shake stage can deliver its wave to the edge of its own weapon's blast.")]
		public void EveryShakeStageCanReachTheEdgeOfItsOwnBlast()
		{
			var info = new ScreenShakerInfo();

			var weaponsWithBlast = 0;
			var stagesChecked = 0;
			var slowStages = 0;
			var offenders = new List<string>();

			foreach (var file in WeaponFiles())
			{
				foreach (var weapon in MiniYaml.FromFile(file))
				{
					// The distance the thing being modelled actually travels, taken from the same
					// weapon's own shockwave rather than from a number written down twice.
					var reach = 0;
					foreach (var wh in weapon.Value.Nodes.Where(n =>
						n.Key.StartsWith("Warhead", StringComparison.Ordinal) && n.Value.Value == "ShockwaveDamage"))
						if (WDist.TryParse(Field(wh, "MaxRadius"), out var r))
							reach = Math.Max(reach, r.Length);

					if (reach <= 0)
						continue;

					weaponsWithBlast++;

					foreach (var wh in weapon.Value.Nodes.Where(n =>
						n.Key.StartsWith("Warhead", StringComparison.Ordinal) && n.Value.Value == "ShakeScreen"))
					{
						stagesChecked++;

						var speed = float.TryParse(Field(wh, "PropagationTicksPerCell"),
							System.Globalization.NumberStyles.Float,
							System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0f
							? s
							: info.PropagationTicksPerCell;

						var cap = int.TryParse(Field(wh, "MaxPropagationDelay"), out var c) && c > 0
							? c
							: info.MaxPropagationDelay;

						if (speed > info.PropagationTicksPerCell)
							slowStages++;

						var needed = speed * reach / 1024f;
						if (cap < needed)
							offenders.Add($"{weapon.Key} {wh.Key}: front is {needed:F0} ticks wide " +
								$"({speed} t/cell over {reach / 1024f:F0} cells) but the arrival ceiling is " +
								$"{cap}, so every camera past {cap / speed:F1} cells gets this stage at the " +
								$"same flat {cap} ticks. Set MaxPropagationDelay to at least {Math.Ceiling(needed)}.");
					}
				}
			}

			// The walk has to be shown to be walking something. All three of these have been the
			// silent-pass mode of a fixture in this tree before.
			Assert.That(weaponsWithBlast, Is.GreaterThan(5),
				$"Only {weaponsWithBlast} weapons with a ShockwaveDamage warhead were found.");
			Assert.That(stagesChecked, Is.GreaterThan(20),
				$"Only {stagesChecked} ShakeScreen stages were found — the parse is wrong, not the mod clean.");
			Assert.That(slowStages, Is.GreaterThan(0),
				"No stage overrides PropagationTicksPerCell, so nothing here is near the ceiling and " +
				"this test is only asserting that the fast ground wave is fast.");

			Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
		}

		[TestCase(TestName = "A per-effect AttenuationDistance gives a small event a shorter horizon.")]
		public void PerEffectAttenuationShortensTheHorizon()
		{
			var m = Model();

			var global = Params(45, 4);
			var tight = Params(45, 4);
			tight.AttenuationDistance = new WDist(18 * Cell);

			Assert.That(m.Falloff(tight, 0), Is.EqualTo(m.Falloff(global, 0)).Within(0.001f),
				"At the epicentre the horizon lever must change nothing.");
			Assert.That(m.Falloff(tight, 40 * Cell), Is.LessThan(0.5f * m.Falloff(global, 40 * Cell)),
				"A tight horizon should have largely died out where the global one is still audible.");
		}

		[TestCase(TestName = "The envelope rises fast, decays, and reaches exactly zero at expiry.")]
		public void EnvelopeHasNoCliff()
		{
			var p = Params(200, 12);

			Assert.That(ScreenShakeModel.Envelope(p, -1), Is.EqualTo(0f));
			Assert.That(ScreenShakeModel.Envelope(p, 200), Is.EqualTo(0f));
			Assert.That(ScreenShakeModel.Envelope(p, 400), Is.EqualTo(0f));

			// Peak is early — the onset is the event, not the middle of it.
			var peakAt = 0;
			var peak = 0f;
			for (var t = 0; t < p.Duration; t++)
			{
				var v = ScreenShakeModel.Envelope(p, t);
				if (v > peak)
				{
					peak = v;
					peakAt = t;
				}
			}

			Assert.That(peak, Is.EqualTo(1f).Within(0.02f));
			Assert.That(peakAt, Is.LessThan(p.Duration / 10));

			// And it lands softly: the last few ticks must already be a small fraction of peak, so
			// removal on the expiry tick is not a visible step. Upstream stopped at FULL amplitude.
			Assert.That(ScreenShakeModel.Envelope(p, p.Duration - 1), Is.LessThan(0.05f));

			// Monotone once past the attack.
			for (var t = 3; t < p.Duration - 1; t++)
				Assert.That(ScreenShakeModel.Envelope(p, t + 1),
					Is.LessThanOrEqualTo(ScreenShakeModel.Envelope(p, t) + 0.0001f));
		}

		[TestCase(TestName = "DecayHalfLife lengthens the tail without raising the peak.")]
		public void DecayHalfLifeIsTheTailLever()
		{
			var shortTail = Params(200, 12, halfLife: 20);
			var longTail = Params(200, 12, halfLife: 90);

			static float Peak(ShakeParams p)
			{
				var peak = 0f;
				for (var t = 0; t < p.Duration; t++)
					peak = Math.Max(peak, ScreenShakeModel.Envelope(p, t));
				return peak;
			}

			// Same peak: the lever buys a longer tail, it does not buy a harder hit.
			Assert.That(Peak(shortTail), Is.EqualTo(Peak(longTail)).Within(0.05f));

			// But a much bigger tail.
			Assert.That(ScreenShakeModel.Envelope(longTail, 100),
				Is.GreaterThan(4 * ScreenShakeModel.Envelope(shortTail, 100)));
		}

		[TestCase(TestName = "Displacement never exceeds Intensity, and stacking is soft-clamped.")]
		public void AmplitudeIsBounded()
		{
			var m = Model();
			var e = Effect(Params(120, 10));

			for (var t = 0; t < 120; t++)
			{
				var s = m.Sample(e, t, 0);
				Assert.That(Math.Abs(s.X), Is.LessThanOrEqualTo(10.001f));
				Assert.That(Math.Abs(s.Y), Is.LessThanOrEqualTo(10.001f));
			}

			var info = new ScreenShakerInfo();
			var huge = m.ClampTotal(new float2(500, -500));
			Assert.That(Math.Abs(huge.X), Is.LessThanOrEqualTo(info.MaxAmplitude));
			Assert.That(Math.Abs(huge.Y), Is.LessThanOrEqualTo(info.MaxAmplitude));

			// Transparent well below the ceiling: a single ordinary event must not be compressed.
			var small = m.ClampTotal(new float2(3, 2));
			Assert.That(small.X, Is.EqualTo(3f).Within(0.15f));
			Assert.That(small.Y, Is.EqualTo(2f).Within(0.1f));
		}

		[TestCase(TestName = "Sampling is deterministic and position-dependent, with no RNG anywhere.")]
		public void WaveformIsDeterministicWithoutRandomness()
		{
			var m = Model();
			var a = Effect(Params(90, 10), spawn: 5, pos: new WPos(10 * Cell, 20 * Cell, 0));
			var b = Effect(Params(90, 10), spawn: 5, pos: new WPos(80 * Cell, 20 * Cell, 0));

			// Same effect, sampled twice, identical to the bit.
			for (var t = 5; t < 90; t++)
				Assert.That(m.Sample(a, t, 0).X, Is.EqualTo(m.Sample(a, t, 0).X));

			// Two detonations in different places must not produce the same waveform, or every
			// nuke sounds like the same nuke.
			var differs = false;
			for (var t = 5; t < 90 && !differs; t++)
				if (Math.Abs(m.Sample(a, t, 0).X - m.Sample(b, t, 0).X) > 0.05f)
					differs = true;

			Assert.That(differs, Is.True, "Two effects at different positions produced the same waveform.");
		}

		[TestCase(TestName = "An effect contributes exactly nothing before arrival and after expiry.")]
		public void ContributesNothingOutsideItsWindow()
		{
			var m = Model();
			var e = Effect(Params(60, 12), spawn: 100);
			var distance = 40 * Cell;
			var arrival = 100 + m.ArrivalDelay(e.Params, distance);

			Assert.That(arrival, Is.GreaterThan(100), "This case is only meaningful with a real delay.");

			for (var t = 100; t < arrival; t++)
				Assert.That(m.Sample(e, t, distance), Is.EqualTo(float2.Zero),
					$"Tick {t} is before the wave arrives at {arrival}.");

			for (var t = arrival + 60; t < arrival + 200; t++)
				Assert.That(m.Sample(e, t, distance), Is.EqualTo(float2.Zero));
		}

		[TestCase(TestName = "Trace: amplitude over time for the shipped shake profiles.")]
		public void TabulateShippedProfiles()
		{
			var m = Model();

			// The shipped call sites. Kept in step with weapons-superweapons.yaml and
			// structures.yaml by hand: this table is a viewing aid, not a binding, so a drift here
			// misleads a reader but breaks nothing.
			//
			// The two nuclear profiles were retimed on 2026-09-06 when both weapons were rebuilt
			// around their stated yields. The tactical nuke's blast wave now ends at tick 89 rather
			// than 213, so its shake is shorter; the strategic one now separates from it in
			// FREQUENCY (0.50 Hz against 1.30) rather than only in amplitude, because peak
			// displacement saturates against the model's 22 px ceiling long before 300x of yield
			// has been spent. Both air-blast stages also carry each weapon's own MEAN front speed
			// now -- the wave leaves the fireball supersonic, so 7.8 t/cell was only its terminal
			// value.
			static ShakeParams Air(ShakeParams p, float ticksPerCell)
			{
				p.PropagationTicksPerCell = ticksPerCell;
				return p;
			}

			static ShakeParams Horizon(ShakeParams p, int cells)
			{
				p.AttenuationDistance = new WDist(cells * Cell);
				return p;
			}

			var profiles = new[]
			{
				("Atomic S1 precursor", Params(20, 4, halfLife: 6, attack: 1, freqScale: 175), 0, 5),
				("Atomic S2 main shock", Params(60, 13, halfLife: 14, attack: 3), 0, 10),
				("Atomic S3 coda", Params(130, 6, halfLife: 45, attack: 20, freqScale: 55), 0, 20),
				("Atomic S4 air blast", Air(Params(45, 5, halfLife: 16, freqScale: 130), 5.9f), 0, 10),
				("Atomic S2 @10 cells", Params(60, 13, halfLife: 14, attack: 3), 10 * Cell, 10),
				("Atomic S4 air @10c", Air(Params(45, 5, halfLife: 16, freqScale: 130), 5.9f), 10 * Cell, 10),
				("HighYield S2 main", Params(110, 20, halfLife: 26, attack: 4, freqScale: 48), 0, 10),
				("HighYield S3 train", Params(380, 9, halfLife: 140, attack: 40, freqScale: 26), 0, 40),
				("HighYield S4 coda", Params(450, 4, halfLife: 170, attack: 60, freqScale: 17), 0, 50),
				("HighYield S5 air blast", Air(Params(120, 7, halfLife: 32, freqScale: 60), 5.8f), 0, 10),
				("HighYield S2 @60c", Params(110, 20, halfLife: 26, attack: 4, freqScale: 48), 60 * Cell, 10),
				("Building collapse", Horizon(Params(40, 3, halfLife: 9), 18), 0, 5),
				("Building @12 cells", Horizon(Params(40, 3, halfLife: 9), 18), 12 * Cell, 5),
			};

			Console.WriteLine();
			Console.WriteLine("SHAKE PROFILE TRACE — peak displacement in screen px over time");
			Console.WriteLine("(Timestep 60 ms. t is measured from ARRIVAL, not from detonation.");
			Console.WriteLine(" Frequencies are the centre of the per-event detune band; live events vary +/-6%.)");
			Console.WriteLine();

			foreach (var (name, p, dist, stride) in profiles)
			{
				var e = Effect(p);
				var delay = m.ArrivalDelay(p, dist);
				var f0 = m.Fundamental(p, dist, JitterCentre);

				// True peak over the whole envelope, not the t=0 value — the attack ramp means t=0
				// is only part of the way up, and normalising the bars to that overflows the gutter.
				var peak = 0f;
				for (var t = 0; t < p.Duration; t++)
					peak = Math.Max(peak, m.Amplitude(e, t, dist));

				Console.WriteLine($"{name,-21} peak {peak,5:F2} px   {p.Duration,4} t ({p.Duration * 0.06,4:F1} s)   " +
					$"half-life {(p.DecayHalfLife > 0 ? p.DecayHalfLife : p.Duration / 4),3} t   " +
					$"arrives +{delay,3} t   {f0:F3} c/t = {f0 / 0.06f:F2} Hz");

				for (var t = 0; t < p.Duration; t += stride)
				{
					var a = m.Amplitude(e, t, dist);
					var n = (int)Math.Round(a / Math.Max(0.001f, peak) * 44);
					Console.WriteLine($"   +{t,4} |{new string('#', Math.Max(0, n))}{new string(' ', Math.Max(0, 44 - n))}| {a,6:F2} px");
				}

				Console.WriteLine();
			}
		}
	}
}
