#region Copyright & License Information
/*
 * WW3MOD nuclear weapons — every radius and delay derived from a stated yield.
 *
 * The two nuclear warheads used to be sized by feel and then described with a yield that did not
 * match. This file is the arithmetic that connects them, run against the SHIPPED YAML rather than a
 * copy of it, so that editing a radius without editing its derivation fails here.
 *
 * THE SCALE. Blast and explosion extents use 160 m per cell (weapon RANGES do not — they use a
 * fourth-root compression, and the two scales are deliberately different; see the comment block on
 * `Atomic`). Timestep is 60 ms, so 16.667 ticks per second — NOT the 25 that TestHarness carries as
 * an AssertWithin budgeting convention.
 *
 * THE FOUR LAWS, and the exponent each one uses:
 *
 *   blast     R_cells = 10 * Y[kt]^(1/3) * P[psi]^-0.589   Hopkinson-Cranz cube root, fitted to the
 *                                                          standard optimum-airburst table
 *   fireball  R_m = 33.53 * Y^0.40,  t_s = 0.2104 * Y^0.44  fitted to the user's breakaway data
 *   thermal   R_km = 0.5593 * Y^0.41                        3rd-degree burn radius
 *   flash     t_min = 0.0025*sqrt(Y) s, t_max = 0.032*sqrt(Y) s   double-flash timing
 *
 * The interesting consequence, and the one the tests below actually pin, is that thermal (Y^0.41)
 * and blast (Y^0.33) CROSS at a few hundred kilotons: the tactical weapon breaks things further than
 * it burns them and the strategic one burns further than it breaks them. That asymmetry is the
 * difference in character between the two, and it exists only because two different exponents are
 * used rather than one scaling factor.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Lighting;
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Test
{
	[TestFixture]
	public class NuclearYieldTest
	{
		const double MetresPerCell = 160.0;
		const double TicksPerSecond = 1000.0 / 60.0;

		const double TacticalKt = 20.0;
		const double StrategicKt = 6000.0;

		// R_cells = 10 * Y^(1/3) * P^-0.589, at 160 m/cell.
		static double BlastCells(double kt, double psi) => 10.0 * Math.Pow(kt, 1 / 3.0) * Math.Pow(psi, -0.5891);
		static double BlastPsi(double kt, double cells) => Math.Pow(10.0 * Math.Pow(kt, 1 / 3.0) / cells, 1 / 0.5891);
		static double FireballCells(double kt) => 33.5311 * Math.Pow(kt, 0.40) / MetresPerCell;
		// 0.8 s at 20 kt, which is the calculator anchor the arsenal is built on: 0.8 / 20^0.44 = 0.21411.
		// This was 0.21045, taken from weapons-superweapons.yaml's own note, and it is 1.7% low against
		// that anchor. The gap is invisible under a one-tick tolerance below ~100 kt and is 7 ticks at
		// Tsar Bomba's 50 Mt, which is where it surfaced. See the tolerance note in the fireball test.
		static double FireballTicks(double kt) => 0.21411 * Math.Pow(kt, 0.44) * TicksPerSecond;
		static double ThermalCells(double kt) => 0.5593 * Math.Pow(kt, 0.41) * 1000.0 / MetresPerCell;

		/// <summary>Rankine-Hugoniot: shock Mach number from peak overpressure, at 14.7 psi ambient.</summary>
		static double Mach(double psi) => Math.Sqrt(1 + 6 / 7.0 * psi / 14.7);

		/// <summary>
		/// The wavefront integration ShockwaveEffect.Tick runs, reproduced exactly — same integer
		/// operands, same integer divisions, same order. Every arrival tick quoted in the YAML comes
		/// from this, so if the engine's loop is ever rewritten in floating point the numbers here
		/// stop matching and someone finds out.
		/// </summary>
		static List<int> Wavefront(int startRadius, int maxRadius, int waveSpeed, int initialSpeedPercent, int decayPercent, int transitionRadius = 0)
		{
			var step = 1024 / waveSpeed;
			var radius = startRadius;
			var excess = (initialSpeedPercent - 100) * 10;
			var history = new List<int> { radius };
			while (radius <= maxRadius && history.Count < 12000)
			{
				radius += step * (1000 + excess) / 1000;
				excess = transitionRadius > 0
					? ExcessPermilleAt(radius, startRadius, transitionRadius, initialSpeedPercent)
					: excess * decayPercent / 100;
				history.Add(radius);
			}

			return history;
		}

		/// <summary>
		/// ShockwaveDamageWarhead.ExcessPermilleAt, reproduced. Deliberately a COPY rather than a call
		/// into the warhead: this file exists to check the shipped YAML against arithmetic written out
		/// longhand, and a test that asks the implementation what the implementation does would pass
		/// through any rewrite of it. The two are pinned to each other by
		/// TheTestsCopyOfTheDecayLawIsTheEnginesCopy below.
		/// </summary>
		static int ExcessPermilleAt(int radius, int startRadius, int transitionRadius, int initialSpeedPercent)
		{
			var remaining = transitionRadius - radius;
			return remaining <= 0 ? 0 : (initialSpeedPercent - 100) * 10 * remaining / (transitionRadius - startRadius);
		}

		/// <summary>
		/// Tick at which the front first reaches <paramref name="cells"/>, plus the start delay. Past the
		/// end of the wave's own travel it EXTRAPOLATES at the terminal sonic speed rather than failing:
		/// every nuclear weapon in the mod puts its outermost suppression band beyond MaxRadius on
		/// purpose, because the wind outlives the overpressure, and that band still needs a delay that
		/// means something. Whether a band is allowed to be out there is checked separately below.
		/// </summary>
		static int ArrivalTick(List<int> history, double cells, int startDelay, int waveSpeed)
		{
			for (var t = 0; t < history.Count; t++)
				if (history[t] >= cells * 1024)
					return t + startDelay;

			var overshoot = cells * 1024 - history[history.Count - 1];
			return history.Count - 1 + startDelay + (int)Math.Round(overshoot * waveSpeed / 1024.0);
		}

		// ---- reading the shipped YAML ----

		static string FindRules(params string[] parts)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(parts).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/rules/{string.Join("/", parts)}");
		}

		static readonly string[] WeaponFiles = { "weapons-superweapons.yaml", "weapons-nuclear-arsenal.yaml" };

		static MiniYaml Weapon(string name)
		{
			var node = WeaponFiles
				.SelectMany(f => MiniYaml.FromFile(FindRules("weapons", f)))
				.FirstOrDefault(n => n.Key == name);
			Assert.That(node, Is.Not.Null, $"{name} is not defined in any of {string.Join(", ", WeaponFiles)} — this test is scanning nothing");
			return node.Value;
		}

		/// <summary>
		/// Every nuclear weapon in the mod and its stated yield, superweapons and arsenal together.
		///
		/// THIS LIST IS THE POINT OF THE 2026-09-07 REVISION. The tests below used to iterate a
		/// hard-coded pair, "Atomic" and "AtomicHighYield", so the eight arsenal weapons added a day
		/// later were checked by NOTHING — they shipped with no StartRadius, no InitialSpeedPercent
		/// and no transition at all, i.e. a point-source wave travelling at a flat sound speed, which
		/// is the exact defect the superweapon pair had just been fixed for. A green suite said so
		/// too, because the pair it looked at were still right. Anything added to either file has to
		/// be added here.
		/// </summary>
		static readonly (string Weapon, double Kt)[] AllNukes =
		{
			("Atomic", TacticalKt),
			("AtomicHighYield", StrategicKt),
			("NukeB61Mod12Y003", 0.3),
			("NukeB61Mod12Y015", 1.5),
			("NukeB61Mod12Y10", 10.0),
			("NukeB61Mod12Y50", 50.0),
			("NukeW76", 100.0),
			("NukeSarmatRV", 750.0),
			("NukeB83", 1200.0),
			("NukeTsarBomba", 50000.0),

			// The Russian half, added 2026-09-07. Three of these four are physically identical to a US
			// weapon at the same yield (Iskander/B61Y10, Kinzhal-N/B61Y50, Kalibr/W76) and are listed
			// separately anyway: "it is a copy" is a claim about how the YAML was WRITTEN, and this list
			// exists to check what the YAML SAYS. A later edit to one of the six that missed its twin is
			// exactly the divergence worth catching, and the monotone-Mach ordering below only sees a
			// weapon that is named here.
			("NukeRu9M729", 1.0),
			("NukeRuIskander", 10.0),
			("NukeRuKinzhalN", 50.0),
			("NukeRuKalibr", 100.0),
		};

		static MiniYaml Warhead(string weapon, string warhead)
		{
			var node = Weapon(weapon).Nodes.FirstOrDefault(n => n.Key == warhead);
			Assert.That(node, Is.Not.Null, $"{weapon} has no {warhead}");
			return node.Value;
		}

		static string Field(MiniYaml warhead, string key, string what)
		{
			var raw = warhead.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value;
			Assert.That(raw, Is.Not.Null, $"{what} has no {key}");
			return raw;
		}

		static int Dist(MiniYaml warhead, string key, string what)
		{
			var raw = Field(warhead, key, what);
			Assert.That(WDist.TryParse(raw, out var d), Is.True, $"{what}'s {key} ('{raw}') is not a WDist");
			return d.Length;
		}

		static int Int(MiniYaml warhead, string key, string what) => int.Parse(Field(warhead, key, what));

		static double Cells(int wdist) => wdist / 1024.0;

		// =============================================================================================

		/// <summary>
		/// The claim that made this whole retune coherent: the two weapons' blast radii, chosen years
		/// apart and for entirely different reasons, sit on the SAME iso-pressure contour. If they did
		/// not, one of the two yields would be wrong.
		/// </summary>
		[Test]
		public void BothNukesOuterEdgesSitOnOneIsoPressureContour()
		{
			var tactical = Cells(Dist(Warhead("Atomic", "Warhead@BlastWave"), "MaxRadius", "Atomic blast"));
			var strategic = Cells(Dist(Warhead("AtomicHighYield", "Warhead@BlastWave"), "MaxRadius", "AtomicHighYield blast"));

			var pT = BlastPsi(TacticalKt, tactical);
			var pS = BlastPsi(StrategicKt, strategic);

			Assert.That(pT, Is.EqualTo(2.74).Within(0.05), $"Atomic's {tactical}c is {pT:F2} psi at 20 kt, not the ~2.7 psi contour");
			Assert.That(pS, Is.EqualTo(2.67).Within(0.05), $"AtomicHighYield's {strategic}c is {pS:F2} psi at 6 Mt, not the ~2.7 psi contour");

			// And therefore the ratio is the cube root of the yield ratio, to within rounding.
			Assert.That(strategic / tactical, Is.EqualTo(Math.Pow(StrategicKt / TacticalKt, 1 / 3.0)).Within(0.15),
				"the two radii are no longer a cube-root-consistent pair; one of the two stated yields is wrong");

			// The brief that asked for this called 15c0 the 1 psi contour. It is not, and the figure
			// below is why the LABEL was corrected rather than the radius: 1 psi at 20 kt is 27 cells,
			// which is almost exactly what the weapon shipped with (30c0) and was being called wrong.
			Assert.That(BlastCells(TacticalKt, 1.0), Is.EqualTo(27.1).Within(0.2));
			Assert.That(BlastCells(StrategicKt, 1.0), Is.EqualTo(181.7).Within(1.0));
		}

		/// <summary>
		/// Thermal grows as Y^0.41 and blast as Y^0.33, so which one reaches further depends on yield.
		/// The tactical weapon must land on one side of that crossover and the strategic one on the
		/// other, or the two stop being different in kind and become the same weapon at two sizes.
		/// </summary>
		[Test]
		public void TheTacticalNukeBreaksFurtherThanItBurnsAndTheStrategicOneDoesNot()
		{
			// Thermal reach is where each weapon's outermost thermal warhead actually stops: the
			// ThermalRadiation falloff table's last non-zero step is index 10 and it crosses zero at
			// index 11, so 11 * Spread is the radius past which nothing is burned.
			double ThermalReach(string weapon) =>
				Cells(Dist(Warhead(weapon, "Warhead@ThermalRadiation"), "Spread", weapon + " thermal")) * 11;

			double BlastReach(string weapon) =>
				Cells(Dist(Warhead(weapon, "Warhead@BlastWave"), "MaxRadius", weapon + " blast"));

			var tacticalThermal = ThermalReach("Atomic");
			var tacticalBlast = BlastReach("Atomic");
			var strategicThermal = ThermalReach("AtomicHighYield");
			var strategicBlast = BlastReach("AtomicHighYield");

			Assert.That(tacticalThermal, Is.EqualTo(ThermalCells(TacticalKt)).Within(0.5),
				"Atomic's thermal reach no longer matches the Y^0.41 law");
			Assert.That(strategicThermal, Is.EqualTo(ThermalCells(StrategicKt)).Within(3.0),
				"AtomicHighYield's thermal reach no longer matches the Y^0.41 law");

			Assert.That(tacticalBlast, Is.GreaterThan(tacticalThermal),
				$"a 20 kt weapon must break ({tacticalBlast}c) further than it burns ({tacticalThermal}c)");
			Assert.That(strategicThermal, Is.GreaterThan(strategicBlast),
				$"a 6 Mt weapon must burn ({strategicThermal}c) further than it breaks ({strategicBlast}c)");

			// Where the two laws cross, solved from 10*Y^(1/3)*2.7^-0.589 == 0.5593*6.25*Y^0.41.
			var crossover = Math.Exp(Math.Log(BlastCells(1, 2.7) / (ThermalCells(1))) / (0.41 - 1 / 3.0));
			Assert.That(crossover, Is.EqualTo(437).Within(20),
				"the thermal/blast crossover moved; the two weapons may no longer straddle it");
			Assert.That(TacticalKt, Is.LessThan(crossover));
			Assert.That(StrategicKt, Is.GreaterThan(crossover));
		}

		/// <summary>
		/// The shockwave starts at the fireball surface rather than at a point, and starts supersonic.
		/// Both numbers are computed from the yield, not chosen, and this is that computation.
		/// </summary>
		[Test]
		public void EachShockwaveIsBornAtItsOwnFireballSurfaceAtItsOwnMachNumber()
		{
			foreach (var (weapon, kt) in AllNukes)
			{
				var w = Warhead(weapon, "Warhead@BlastWave");
				var startRadius = Cells(Dist(w, "StartRadius", weapon));
				var initialSpeed = Int(w, "InitialSpeedPercent", weapon);

				// RELATIVE tolerance, and the reason is worth knowing: the two weapon files write the
				// SAME fireball law with differently-rounded constants — weapons-superweapons.yaml and
				// this file use 33.5311 * Y^0.40 metres, weapons-nuclear-arsenal.yaml's header uses
				// 111 * (Y/20)^0.40, and 33.5311 * 20^0.40 is 111.14, not 111. That is a 0.13%
				// disagreement, invisible at 0.3 kt and 0.02 cells at 50 Mt — which is to say it sits
				// exactly on an 0.02-cell absolute tolerance and tips Tsar Bomba over it. A percentage
				// is the honest tolerance for a percentage-sized discrepancy, and it is STRICTER than
				// the absolute one everywhere below about 4 Mt. Reconciling the two constants would
				// move fireball durations, sprite scales and thermal radii across eight weapons, so it
				// is recorded rather than done here.
				Assert.That(startRadius, Is.EqualTo(FireballCells(kt)).Within(0.5).Percent,
					$"{weapon}'s wave starts at {startRadius}c but its fireball breaks away at {FireballCells(kt):F3}c");

				// The overpressure the blast law puts at the breakaway radius, through Rankine-Hugoniot.
				var mach = Mach(BlastPsi(kt, startRadius));
				Assert.That(initialSpeed, Is.EqualTo((int)Math.Round(mach * 100)).Within(2),
					$"{weapon}'s InitialSpeedPercent {initialSpeed} is not the Mach {mach:F2} its own breakaway overpressure implies");

				// A point source is the defect this whole file exists to catch. Spell it out, because
				// "StartRadius is absent" and "StartRadius is 0" reach the engine as the same thing.
				Assert.That(startRadius, Is.GreaterThan(0),
					$"{weapon}'s shockwave starts at a point; inside a fireball there is no wave to propagate");
			}

			// Breakaway Mach FALLS with yield, because a larger fireball breaks away at a lower
			// overpressure. Across the whole arsenal, not just the pair: if this ever becomes monotonic
			// the other way, someone has "fixed" the thing that makes a big weapon feel heavy.
			var byYield = AllNukes
				.OrderBy(n => n.Kt)
				.Select(n => Int(Warhead(n.Weapon, "Warhead@BlastWave"), "InitialSpeedPercent", n.Weapon))
				.ToArray();
			for (var i = 1; i < byYield.Length; i++)
				Assert.That(byYield[i], Is.LessThanOrEqualTo(byYield[i - 1]),
					$"breakaway Mach rises with yield somewhere in {string.Join(", ", byYield)}; it falls");

		}

		/// <summary>
		/// THE SUPERSONIC PHASE ENDS AT TWICE THE FIREBALL RADIUS, on every weapon. That multiple is the
		/// whole model: the shock is attached to the fireball while the fireball is supersonic, detaches
		/// at breakaway, and is decayed to sound speed by about twice that radius.
		///
		/// The predecessor of this test could not have been written, because the transition was not a
		/// number in the YAML — it was implied by a per-weapon SpeedDecayPercent that decayed in TIME,
		/// and you had to integrate to find out where it landed. It landed at 5.0x the fireball radius
		/// on Atomic and 3.6x on AtomicHighYield: same intent, two different answers, neither of them 2.
		/// </summary>
		[Test]
		public void TheSupersonicPhaseEndsAtTwiceTheFireballRadiusAndTheFrontIsSonicAfterIt()
		{
			foreach (var (weapon, kt) in AllNukes)
			{
				var w = Warhead(weapon, "Warhead@BlastWave");
				var startRadius = Dist(w, "StartRadius", weapon);
				var transition = Dist(w, "TransitionRadius", weapon);

				Assert.That(Cells(transition), Is.EqualTo(2 * Cells(startRadius)).Within(0.01),
					$"{weapon} transitions at {Cells(transition):F2}c, which is {Cells(transition) / Cells(startRadius):F2}x its fireball radius, not 2x");

				// The old law is gone from this weapon, not merely outvoted by the new one. Setting both
				// throws at load, so a leftover here is a mod that does not start.
				Assert.That(w.Nodes.Any(n => n.Key == "SpeedDecayPercent"), Is.False,
					$"{weapon} still carries SpeedDecayPercent alongside TransitionRadius; the two are different decay laws and the engine refuses the pair");

				var waveSpeed = Int(w, "WaveSpeed", weapon);
				var history = Wavefront(startRadius, Dist(w, "MaxRadius", weapon), waveSpeed,
					Int(w, "InitialSpeedPercent", weapon), 100, transition);

				// The two-phase shape, stated as invariants rather than as a table of arrival ticks that
				// drifts the moment anything upstream is retuned: born supersonic, never subsonic,
				// decelerating monotonically, and exactly sonic from the transition onward.
				//
				// NOTE WHAT IS NOT ASSERTED: that every step INSIDE the transition is strictly
				// supersonic. In the last sliver before it the excess is a permille figure divided by
				// the span, and integer division floors it to zero a few wdist short of the boundary —
				// so the final pre-transition step is already exactly sonic. That is the arithmetic
				// working, not failing, and an assertion that forbade it would be pinning a rounding
				// artefact.
				var sonicStep = 1024 / waveSpeed;
				Assert.That(history[1] - history[0], Is.GreaterThan(sonicStep),
					$"{weapon}'s front is born sonic; it should leave the fireball at Mach {Int(w, "InitialSpeedPercent", weapon) / 100.0:F2}");

				for (var t = 1; t < history.Count; t++)
				{
					var step = history[t] - history[t - 1];
					Assert.That(step, Is.GreaterThanOrEqualTo(sonicStep),
						$"{weapon} is travelling below sound speed at {Cells(history[t - 1]):F1}c; a shock in air decays TO sound, not through it");

					if (t > 1)
						Assert.That(step, Is.LessThanOrEqualTo(history[t - 1] - history[t - 2]),
							$"{weapon} accelerates at {Cells(history[t - 1]):F1}c; the front only ever decelerates");

					if (history[t - 1] >= transition)
						Assert.That(step, Is.EqualTo(sonicStep),
							$"{weapon} is still travelling at {1024.0 / step:F2} cells/tick at {Cells(history[t - 1]):F1}c, past its {Cells(transition):F1}c transition");
				}

				// And the fast phase is genuinely fast — it beats sound over its own length. Below about
				// 50 kt it is shorter than one tick, which is why this is the assertion and not a
				// minimum duration: the phase is real at every yield, visible only at the large ones.
				var fastTicks = history.FindIndex(r => r >= transition);
				var sonicTicks = (transition - startRadius) / sonicStep;
				Assert.That(fastTicks, Is.LessThan(Math.Max(2, sonicTicks)),
					$"{weapon}'s supersonic phase takes {fastTicks} ticks to cross what sound crosses in {sonicTicks}");
			}
		}

		/// <summary>
		/// The copy of the decay law above and the engine's own are the same function. Checked over the
		/// shipped weapons' actual parameters plus the edges, because the copy exists precisely so that
		/// a rewrite of the engine's version cannot quietly take the tests with it.
		/// </summary>
		[Test]
		public void TheTestsCopyOfTheDecayLawIsTheEnginesCopy()
		{
			foreach (var (weapon, _) in AllNukes)
			{
				var w = Warhead(weapon, "Warhead@BlastWave");
				var start = Dist(w, "StartRadius", weapon);
				var transition = Dist(w, "TransitionRadius", weapon);
				var initial = Int(w, "InitialSpeedPercent", weapon);

				var engine = new ShockwaveDamageWarhead();
				var f = typeof(ShockwaveDamageWarhead);
				f.GetField("StartRadius").SetValue(engine, new WDist(start));
				f.GetField("TransitionRadius").SetValue(engine, new WDist(transition));
				f.GetField("InitialSpeedPercent").SetValue(engine, initial);

				foreach (var r in new[] { start, start + 1, (start + transition) / 2, transition - 1, transition, transition + 1, transition * 4 })
					Assert.That(engine.ExcessPermilleAt(r), Is.EqualTo(ExcessPermilleAt(r, start, transition, initial)),
						$"{weapon}: the engine and this file disagree about the front's excess speed at {Cells(r):F2}c");
			}
		}

		/// <summary>
		/// Every suppression Delay in the YAML claims to be the tick the wavefront reaches that band.
		/// This runs the engine's own integer integration and checks that it is.
		/// </summary>
		[Test]
		public void SuppressionDelaysAreTheTicksTheWavefrontActuallyArrives()
		{
			foreach (var (weapon, _) in AllNukes)
			{
				var blast = Warhead(weapon, "Warhead@BlastWave");
				var waveSpeed = Int(blast, "WaveSpeed", weapon);
				var history = Wavefront(
					Dist(blast, "StartRadius", weapon),
					Dist(blast, "MaxRadius", weapon),
					waveSpeed,
					Int(blast, "InitialSpeedPercent", weapon),
					100,
					Dist(blast, "TransitionRadius", weapon));
				var startDelay = Int(blast, "StartDelay", weapon);
				var maxRadius = Cells(Dist(blast, "MaxRadius", weapon));

				// The two files name their suppression bands differently. Take whichever the weapon has.
				var bands = Weapon(weapon).Nodes
					.Where(n => n.Key.StartsWith("Warhead@Suppression", StringComparison.Ordinal))
					.ToArray();
				Assert.That(bands, Is.Not.Empty, $"{weapon} has no suppression bands — this loop is scanning nothing");

				var beyond = 0;
				foreach (var node in bands)
				{
					var band = node.Key;
					var range = Cells(Dist(node.Value, "Range", $"{weapon} {band}"));
					var delay = Int(node.Value, "Delay", $"{weapon} {band}");
					var arrival = ArrivalTick(history, range, startDelay, waveSpeed);

					if (range > maxRadius)
						beyond++;

					Assert.That(delay, Is.EqualTo(arrival).Within(2),
						$"{weapon} {band} is at {range}c and fires at tick {delay}, but the wavefront gets there at {arrival}");
				}

				// One band past MaxRadius is the wind outliving the overpressure and is intended. Two
				// would mean the blast wave has quietly been shortened underneath a stack of delays
				// that are now extrapolated fiction rather than arrivals.
				Assert.That(beyond, Is.LessThanOrEqualTo(1),
					$"{weapon} has {beyond} suppression bands outside its {maxRadius}c MaxRadius; only the outermost wind band may sit out there");
			}
		}

		/// <summary>
		/// The default path has to be bit-identical to the closed form it replaced, or every existing
		/// shockwave in the mod moves with no YAML change. Same argument as ShockwaveTuningTest's.
		/// </summary>
		[Test]
		public void APointSourceSonicWaveReproducesTheOldClosedFormExactly()
		{
			var w = new ShockwaveDamageWarhead();
			Assert.That(w.StartRadius, Is.EqualTo(WDist.Zero));
			Assert.That(w.InitialSpeedPercent, Is.EqualTo(100));
			Assert.That(w.SpeedDecayPercent, Is.EqualTo(100));
			Assert.That(w.TransitionRadius, Is.EqualTo(WDist.Zero));

			foreach (var waveSpeed in new[] { 2, 3, 5, 7, 10, 25 })
			{
				var history = Wavefront(0, 200 * 1024, waveSpeed, 100, 100, 0);
				var step = 1024 / waveSpeed;
				for (var t = 0; t < history.Count; t++)
					Assert.That(history[t], Is.EqualTo(t * step),
						$"at WaveSpeed {waveSpeed}, tick {t}: the integrated front is no longer ticks * (1024 / WaveSpeed)");
			}
		}

		// ---- the fireball ANIMATION, derived from the assets rather than assumed ----
		//
		// THIS IS THE COUPLING THE 2026-09-07 REWORK EXISTS TO CREATE, so it is computed here rather
		// than written down. The light's duration is now the fireball ANIMATION's duration, and the
		// animation's duration is not a number anyone chose: it falls out of the SHP's frame count
		// walked along the sequence's Tick/ChangeTick ladder. Hard-coding it would let the two drift
		// apart silently, and the drift IS the bug being fixed -- every arsenal weapon shipped with a
		// flash that ended after 1-7% of the cloud it was supposed to be lighting.
		//
		// Reading the SHP header from a YAML test is deliberate and is the cheapest honest option: 8
		// bytes, no decode. The alternative is to trust a comment.

		const int AnimationMillisecondsPerTick = 40;   // Animation.Tick() spends 40 whatever the timestep is
		const double FireballPeak = 7.0;
		const double FireballShoulderFraction = 0.70;

		/// <summary>Locates a file under mods/ww3mod, walking up from the test binary.</summary>
		static string FindMod(params string[] parts)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod" }.Concat(parts).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/{string.Join("/", parts)}");
		}

		/// <summary>Frame count from a TS-format SHP header: u16 zero, u16 width, u16 height, u16 count.</summary>
		static int ShpFrameCount(string path)
		{
			using (var stream = File.OpenRead(path))
			{
				var header = new byte[8];
				Assert.That(stream.Read(header, 0, 8), Is.EqualTo(8), $"{path} is too short to be a SHP");
				Assert.That(BitConverter.ToUInt16(header, 0), Is.EqualTo(0),
					$"{path} is not a TS-format SHP: its leading u16 is not zero, so the frame count is not at offset 6");
				return BitConverter.ToUInt16(header, 6);
			}
		}

		// The explosion sequence a weapon's own Warhead@Fireball draws its cloud with, and the
		// DurationScalePercent that warhead plays it at. Both come from the SAME node on purpose: a
		// weapon may carry several fireball warheads (Tsar Bomba has five, offset into one cloud) and
		// pairing a sequence with another warhead's scale would silently derive the wrong length.
		static (string Sequence, int DurationScalePercent) FireballSequence(string weapon)
		{
			foreach (var node in Weapon(weapon).Nodes)
			{
				if (node.Value.Value != "CreateEffect")
					continue;

				var explosions = node.Value.Nodes.FirstOrDefault(n => n.Key == "Explosions")?.Value.Value;
				if (explosions == null || !explosions.Contains("nuke", StringComparison.Ordinal))
					continue;

				// Absent means 100, which is what CreateEffectWarhead.DurationScalePercent defaults to.
				var raw = node.Value.Nodes.FirstOrDefault(n => n.Key == "DurationScalePercent")?.Value.Value;
				return (explosions.Trim(), raw == null ? 100 : int.Parse(raw));
			}

			Assert.Fail($"{weapon} has no CreateEffect warhead drawing a nuke explosion, so its light has no animation to match");
			return (null, 100);
		}

		/// <summary>
		/// Length of one play-through of an explosion sequence, in GAME ticks.
		///
		/// Animation.CurrentSequenceTickOrDefault walks ChangeTick as (frame, ms) pairs and takes the
		/// LAST pair whose frame is strictly below the current one, falling back to the sequence's
		/// Tick; SpriteEffect.Tick then spends 40 * 100 / DurationScalePercent ms of that budget per
		/// game tick, banking the remainder, regardless of the mod's 60 ms timestep. So the length is
		/// the summed per-frame budget over 40, TIMES that percentage -- NOT the frame count, and NOT
		/// affected by ScalePercent, which scales the sprite in space and not in time.
		/// </summary>
		static double AnimationTicks(string sequence, int durationScalePercent)
		{
			var explosions = MiniYaml.FromFile(FindMod("sequences", "sequences-ingame.yaml"))
				.FirstOrDefault(n => n.Key == "explosion");
			Assert.That(explosions, Is.Not.Null, "sequences-ingame.yaml has no `explosion:` node — this test is scanning nothing");

			var node = explosions.Value.Nodes.FirstOrDefault(n => n.Key == sequence);
			Assert.That(node, Is.Not.Null, $"sequences-ingame.yaml has no explosion sequence `{sequence}`");

			var tick = 40;
			var rawTick = node.Value.Nodes.FirstOrDefault(n => n.Key == "Tick")?.Value.Value;
			if (rawTick != null)
				tick = int.Parse(rawTick);

			int[] change = null;
			var rawChange = node.Value.Nodes.FirstOrDefault(n => n.Key == "ChangeTick")?.Value.Value;
			if (rawChange != null)
				change = rawChange.Split(',').Select(s => int.Parse(s.Trim())).ToArray();

			var frames = ShpFrameCount(FindMod("bits", "weapons", "explosions", node.Value.Value + ".shp"));

			var total = 0;
			for (var f = 0; f < frames; f++)
			{
				var chosen = 0;
				if (change != null)
				{
					for (var i = 0; i + 1 < change.Length; i += 2)
					{
						if (f > change[i])
							chosen = change[i + 1];
						else
							break;
					}
				}

				total += chosen != 0 ? chosen : tick;
			}

			return total * durationScalePercent / (100.0 * AnimationMillisecondsPerTick);
		}

		/// <summary>
		/// The tick at which an envelope's white spike ends -- the first keyframe sitting on the
		/// shoulder value. Found in the YAML rather than recomputed from the yield on purpose: the
		/// tests below then pin the SHAPE without also pinning the rounding rule that picked W, so
		/// retuning W is a one-line change to the generator and not a test edit.
		/// </summary>
		static int ShoulderTick(LightEventDefinition light, string weapon)
		{
			var shoulder = FireballPeak * FireballShoulderFraction;
			for (var k = 1; k < light.Intensities.Length; k++)
				if (Math.Abs(light.Intensities[k] - shoulder) < 0.02)
					return light.Times[k];

			Assert.Fail($"{weapon}'s envelope has no keyframe at the shoulder value {shoulder}, so it is not the two-stage law");
			return 0;
		}

		/// <summary>
		/// The fireball light, on EVERY nuclear weapon in the mod. Loads each shipped envelope through
		/// the same FieldLoader path the game uses — so a mismatched keyframe array fails here rather
		/// than at mod load — and then checks the things that make it a nuclear fireball.
		///
		/// REWRITTEN TWICE ON 2026-09-07, and both rewrites are load-bearing.
		///
		/// The FIRST replaced a DOUBLE FLASH — first pulse, strictly lower dip, strictly larger second
		/// maximum — with a single-peaked monotone decay. A real fireball does double-flash and the
		/// interval between the maxima is how yield is measured from a bhangmeter trace; all of it
		/// renders as a STROBE at a 60 ms timestep, and `Atomic`'s envelope read 2.2 -> 0.8 -> 7.0 on
		/// three consecutive ticks. THAT INVARIANT IS UNCHANGED AND IS STILL PINNED HERE:
		///
		///     PEAK AT KEYFRAME 0, MONOTONE NON-INCREASING THEREAFTER. DO NOT MODEL THE DOUBLE FLASH.
		///
		/// The SECOND is this one, after the user played the result. It changes two things:
		///
		///     DURATION IS THE FIREBALL ANIMATION'S, NOT THE YIELD'S. The old rule took the physical
		///     incandescent lifetime, 0.8*(Y/20)^0.44 s. That is 2 ticks at 0.3 kt against a mushroom
		///     sprite that runs 199, so the flash was over inside 1% of its own cloud and the small
		///     weapons did not read as nuclear at all. The rule is now
		///         D = max(fireball animation length, physical fireball lifetime)
		///     with the animation length DERIVED here from the sequences file and the SHP header. This
		///     is the assertion that keeps the light and the cloud from drifting apart again, which is
		///     the whole point of the rework. The physical term bound only on Tsar Bomba then, and on
		///     nothing at all since 2026-09-08, when the animation itself started scaling with yield
		///     and overtook it — see AnimationTicks and DurationScalePercent.
		///
		///     THE ENVELOPE IS TWO STAGES. A short blinding white spike falling to a shoulder at 70%
		///     of peak, then a long quadratic fade to nothing:
		///         0 <= t <= W:  I = 4.9 + 2.1 * (1 - t/W)^2
		///         W <= t <= D:  I = 4.9 * (1 - (t-W)/(D-W))^2
		///     Both stages fall, so the anti-strobe invariant survives intact; they differ in RATE.
		///     Colour is decoupled from intensity and keyed on t/D, so the light is still bright while
		///     it is already going orange — which is what "goes towards red at the later stages" means
		///     and what a single curve driving both axes cannot express.
		///
		/// It iterates AllNukes rather than the hard-coded superweapon pair. That is not tidiness:
		/// before the arsenal weapons carried lights at all, "both fireballs" meant two of ten, and a
		/// green suite said nothing whatever about the other eight.
		/// </summary>
		[Test]
		public void EveryNuclearFireballIsATwoStageFlashThatCoolsAndLastsAsLongAsItsFireballAnimation()
		{
			foreach (var (weapon, kt) in AllNukes)
			{
				var light = LightEventDefinition.LoadFrom(Warhead(weapon, "Warhead@FireballLight"), "Light", true);
				var fireball = FireballSequence(weapon);
				var animation = AnimationTicks(fireball.Sequence, fireball.DurationScalePercent);
				var physical = FireballTicks(kt);

				// 1. THE LIGHT COVERS THE CLOUD. Stated first and on its own because it is the user's
				//    actual complaint, and a failure here should say so rather than quoting arithmetic.
				Assert.That(light.Duration, Is.GreaterThanOrEqualTo((int)Math.Floor(animation)),
					$"{weapon}'s light lasts {light.Duration} ticks but its fireball animation runs {animation:F0}. " +
					$"The cloud would burn in the dark for the last {animation - light.Duration:F0} ticks, which is the defect this envelope was rewritten to fix.");

				// 2. AND IT IS EXACTLY THE RULE, so a weapon cannot quietly acquire a longer light than
				//    the rule gives. The tolerance is relative for the same reason as ever: two
				//    constants for the 0.8*(Y/20)^0.44 law are live in the tree and differ by 1.7%.
				var expected = Math.Max(animation, physical);
				Assert.That(light.Duration, Is.EqualTo(expected).Within(Math.Max(1.0, 0.025 * expected)),
					$"{weapon}'s light is {light.Duration} ticks against the {expected:F0} that max(animation {animation:F0}, physical {physical:F0}) gives. " +
					"If the sequence ladder moved, regenerate with tools/nuke-light/gen_fireball_light.py --write.");

				var i = light.Intensities;

				// 3. THE ANTI-STROBE INVARIANT. Brightest at tick 0, never brighter again.
				Assert.That(Array.IndexOf(i, i.Max()), Is.EqualTo(0),
					$"{weapon}'s fireball peaks at keyframe {Array.IndexOf(i, i.Max())} rather than at tick 0 — an envelope that gets brighter after it starts reads as a strobe at 60 ms per tick");
				for (var k = 1; k < i.Length; k++)
					Assert.That(i[k], Is.LessThanOrEqualTo(i[k - 1]),
						$"{weapon}'s fireball brightens again between keyframes {k - 1} and {k} ({i[k - 1]} -> {i[k]})");
				Assert.That(i[i.Length - 1], Is.EqualTo(0f), $"{weapon}'s fireball does not fade to nothing");

				// 4. IT IS THE SAME TWO-STAGE LAW ON ALL FOURTEEN, at each weapon's own W and D.
				//    Checking the shape rather than only monotonicity is what makes the set one rule
				//    applied fourteen times instead of fourteen curves that each happen to go down.
				Assert.That(i[0], Is.EqualTo((float)FireballPeak).Within(0.01),
					$"{weapon} does not peak at {FireballPeak}; fireball surface brightness is set by temperature and does not scale with yield");

				var shoulder = FireballPeak * FireballShoulderFraction;
				var w = ShoulderTick(light, weapon);

				// THE SPIKE MUST STAY SHORT. This is the "much shorter initial super-intense light"
				// half of the request, and without it the two-stage law degenerates into a plateau.
				Assert.That(w, Is.InRange(3, 14), $"{weapon}'s white phase is {w} ticks, outside the 3-14 the rule allows");
				Assert.That(w, Is.LessThanOrEqualTo((int)(0.12 * light.Duration)),
					$"{weapon} spends {w} of its {light.Duration} ticks at near-peak brightness; the white spike must be a small fraction of the envelope, not most of it");

				for (var k = 0; k < i.Length; k++)
				{
					var t = light.Times[k];
					double law;
					if (t <= w)
					{
						var f = 1.0 - t / (double)w;
						law = shoulder + (FireballPeak - shoulder) * f * f;
					}
					else
					{
						var u = (t - w) / (double)(light.Duration - w);
						law = shoulder * (1 - u) * (1 - u);
					}

					Assert.That(i[k], Is.EqualTo(law).Within(0.06),
						$"{weapon} keyframe {k} (tick {t}) is {i[k]} against the {law:F2} the two-stage law gives at W={w}, D={light.Duration}");
				}

				// 5. IT COOLS. White -> yellow -> orange -> dull red, monotonically.
				//
				//    The measure is the red:blue RATIO and not red MINUS blue, which is the trap here: a
				//    cooling fireball also darkens, so the difference can fall across a step that is
				//    plainly redder (FF9C3C -> B4280A drops R-B from 195 to 170 while R/B climbs from
				//    4.3 to 18). Difference measures brightness as much as hue; ratio measures hue.
				for (var k = 0; k + 1 < light.Tints.Length; k++)
				{
					var a = light.Tints[k];
					var b = light.Tints[k + 1];
					Assert.That(b.B, Is.LessThanOrEqualTo(a.B), $"{weapon}'s fireball gets BLUER between keyframes {k} and {k + 1}");
					Assert.That((b.R + 1f) / (b.B + 1f), Is.GreaterThanOrEqualTo((a.R + 1f) / (a.B + 1f)),
						$"{weapon}'s fireball stops warming toward red at keyframe {k + 1}");
				}

				var final = light.Tints[light.Tints.Length - 1];
				Assert.That(final.R, Is.GreaterThan(2 * final.B), $"{weapon}'s fireball does not end on a dull red");

				// 6. THE SPIKE IS WHITE AND THE TAIL IS NOT. Both halves are new with the two-stage
				//    envelope and both are the user's words: "while the fireball is active it produces
				//    that white strong light, and then it goes towards red at the later stages". The
				//    monotone-ratio check above allows an envelope that is orange from tick 0, or one
				//    that is still white at the end; neither is what was asked for.
				for (var k = 0; k < light.Tints.Length && k < light.Times.Length && light.Times[k] <= w; k++)
				{
					var c = light.Tints[k];
					var lo = Math.Min(c.R, Math.Min(c.G, c.B));
					var hi = Math.Max(c.R, Math.Max(c.G, c.B));
					Assert.That(lo / (double)hi, Is.GreaterThanOrEqualTo(0.88),
						$"{weapon}'s keyframe {k} is inside the white phase (tick {light.Times[k]} <= {w}) but is {c} — the spike has to read as white");
				}

				var reddenedInTime = false;
				for (var k = 0; k < light.Tints.Length && k < light.Times.Length; k++)
					if (light.Times[k] <= 0.7 * light.Duration && light.Tints[k].R > 2 * light.Tints[k].B)
						reddenedInTime = true;

				Assert.That(reddenedInTime, Is.True,
					$"{weapon} is still not red by 70% of its {light.Duration}-tick envelope; the ramp reaches red too late to read as cooling");

				// 7. The light's maximum radius is the thermal radius: illumination reach scales as the
				//    fireball radius (Y^0.40) and thermal as Y^0.41, close enough that one array carries both.
				Assert.That(Cells(light.MaximumRadius.Length), Is.EqualTo(ThermalCells(kt)).Within(Math.Max(3.0, 0.05 * ThermalCells(kt))),
					$"{weapon}'s light no longer reaches its thermal radius");

				// 8. The flash has to survive fog, or it is invisible over exactly the ground a player
				//    is most likely to be nuking. LightEventManager.RenderAboveFog reads this per event.
				Assert.That(light.GlowAboveFog, Is.True, $"{weapon}'s fireball light is attenuated by fog");
			}
		}

		/// <summary>
		/// THE OTHER HALF OF THE STROBE, and the half the user was most likely actually looking at.
		///
		/// FlashPaletteEffect.Enable ASSIGNS `remainingFrames = ticks` — it does not add, extend or take
		/// a maximum — and AdjustPalette lerps the whole palette toward white by
		/// `frac = remainingFrames / Info.Length`, which ramps DOWN to nothing over the Duration. It is
		/// a one-shot sawtooth. Six weapons used to stack two to five of them at 22-tick spacing to
		/// fake a longer flash; by tick 22 the screen had faded to frac 0.27 and the next call slammed
		/// it back to 1.0 in a single tick. That is a 1.3 Hz square wave, it was live on exactly the
		/// high-yield weapons and not on the small ones, and it is why the report said "in some cases".
		///
		/// Two rules, both pinned here because both are invisible from the YAML alone:
		///   ONE FlashPaletteEffect warhead per weapon. Sustain belongs to the light event, which has a
		///   real envelope; this effect has one counter and one ramp.
		///   Duration &lt;= the effect's Length. Above it frac starts above 1 and the lerp is unclamped,
		///   which is a corrupted palette rather than a longer flash. Nothing shipped violates this
		///   today and this is here to keep it that way.
		/// </summary>
		[Test]
		public void NoNuclearWeaponStacksScreenFlashes()
		{
			var length = NukeFlashLength();

			foreach (var (weapon, _) in AllNukes.Concat(new[] { ("NukeSarmatMIRV", 0.0) }))
			{
				var flashes = Weapon(weapon).Nodes
					.Where(n => n.Value.Value == "FlashPaletteEffect")
					.ToArray();

				Assert.That(flashes.Length, Is.LessThanOrEqualTo(1),
					$"{weapon} fires {flashes.Length} FlashPaletteEffect warheads. Staggered calls do not lengthen the flash, they restart it — see the note on AtomicHighYield's Warhead@Flash");

				foreach (var flash in flashes)
				{
					Assert.That(Int(flash.Value, "Duration", $"{weapon} {flash.Key}"), Is.LessThanOrEqualTo(length),
						$"{weapon} {flash.Key} outlasts the Nuke palette effect's Length of {length}, which extrapolates the lerp past white rather than flashing for longer");
					Assert.That(Field(flash.Value, "FlashType", $"{weapon} {flash.Key}"), Is.EqualTo("Nuke"));
				}
			}
		}

		/// <summary>The Length of the `Nuke` FlashPaletteEffect, read from palettes.yaml rather than assumed.</summary>
		static int NukeFlashLength()
		{
			var palettes = MiniYaml.FromFile(FindRules("palettes.yaml"))
				.FirstOrDefault(n => n.Key == "^Palettes");
			Assert.That(palettes, Is.Not.Null, "palettes.yaml has no ^Palettes node — this test is scanning nothing");

			foreach (var node in palettes.Value.Nodes)
			{
				if (!node.Key.StartsWith("FlashPaletteEffect", StringComparison.Ordinal))
					continue;

				if (node.Value.Nodes.FirstOrDefault(n => n.Key == "Type")?.Value.Value != "Nuke")
					continue;

				var raw = node.Value.Nodes.FirstOrDefault(n => n.Key == "Length")?.Value.Value;
				return raw == null ? 20 : int.Parse(raw);
			}

			Assert.Fail("palettes.yaml has no FlashPaletteEffect with Type: Nuke");
			return 0;
		}

		/// <summary>What yield buys the fireball, and the one axis it deliberately does not.</summary>
		// REWRITTEN 2026-09-07, then again 2026-09-08.
		//
		// The FIRST rewrite deleted an assertion that the two lifetimes stood in the ratio
		// (6000/20)^0.44 — the physical fireball law. Duration had become the fireball ANIMATION's,
		// both weapons drew the same sequence at the same speed, so both durations were the same
		// number and the ratio was 1. What went in instead was the RULE rather than the ratio: each
		// weapon's light is as long as its OWN animation.
		//
		// The SECOND is this one, and it is why this test is no longer called "...ButNotItsLength".
		// The animation itself now scales, at t = 11.95 s * (Y/20)^0.12, so length is back on the list
		// of things yield buys — 394 ticks against 199 for these two. The per-weapon assertion below
		// did not need editing to pick that up, which is the whole reason it was written that way.
		//
		// What yield buys, and what is checked here:
		//   AREA — 124 cells against 12, still on the thermal Y^0.41 law.
		//   LENGTH — 394 ticks against 199, on the animation's law and not the physical one.
		//   THE WHITE PHASE — 9 ticks against 4.
		// What it deliberately does not buy:
		//   BRIGHTNESS — a fireball's surface is ~7000 K whatever set it off, so the peaks must
		//   match. Making the strategic weapon brighter instead of bigger is the one obvious wrong
		//   way to scale this, and it would not even work: TerrainLighting.Tint sums
		//   `falloff * intensity * tint` with no clamp anywhere in the path, so 7.0 is already
		//   saturated at the centre of the light and raising it only widens the blown-out core.
		[Test]
		public void YieldScalesTheFireballsAreaLengthAndWhitePhaseButNotItsBrightness()
		{
			var tactical = LightEventDefinition.LoadFrom(Warhead("Atomic", "Warhead@FireballLight"), "Light", true);
			var strategic = LightEventDefinition.LoadFrom(Warhead("AtomicHighYield", "Warhead@FireballLight"), "Light", true);

			Assert.That(strategic.Intensities.Max(), Is.EqualTo(tactical.Intensities.Max()).Within(0.01),
				"the two fireballs no longer peak at the same brightness; surface temperature does not scale with yield");

			var radiusRatio = (double)strategic.MaximumRadius.Length / tactical.MaximumRadius.Length;
			Assert.That(radiusRatio, Is.EqualTo(Math.Pow(StrategicKt / TacticalKt, 0.41)).Within(1.0));

			// Each light is as long as its OWN animation. Written per weapon rather than as a ratio so
			// it still means something when the two stop sharing a sequence.
			foreach (var (weapon, light) in new[] { ("Atomic", tactical), ("AtomicHighYield", strategic) })
			{
				var fireball = FireballSequence(weapon);
				Assert.That(light.Duration, Is.GreaterThanOrEqualTo((int)Math.Floor(AnimationTicks(fireball.Sequence, fireball.DurationScalePercent))),
					$"{weapon}'s light no longer covers its own fireball animation");
			}

			// And the strategic weapon's cloud really does hang around longer, which is the 2026-09-08
			// request. Asserted as an inequality rather than at the exponent: 0.12 is a taste number
			// and retuning it must not be a test edit, but reversing the ORDER would be the defect.
			Assert.That(strategic.Duration, Is.GreaterThan(tactical.Duration),
				"the 6 Mt fireball no longer outlasts the 20 kt one; yield is supposed to buy a slower cloud as well as a bigger one");

			Assert.That(ShoulderTick(strategic, "AtomicHighYield"), Is.GreaterThan(ShoulderTick(tactical, "Atomic")),
				"the strategic weapon's white phase is no longer longer than the tactical one's; the white phase is the only thing yield still buys in time");
		}

		/// <summary>
		/// LeaveSmudgeWarhead reaches Map.FindTilesInAnnulus, which THROWS above
		/// MapGrid.MaximumTileSearchRange (56) rather than clamping. There is no lint for it, and the
		/// failure is a hard crash on the tick the smudge lands — the same ceiling that killed the game
		/// through CameraRange on 2026-09-06. AtomicHighYield's thermal radius is 124 cells and the
		/// temptation to scorch that far is real.
		///
		/// SCANS AllNukes, NOT A HARD-CODED PAIR. This test used to iterate { "Atomic",
		/// "AtomicHighYield" } — the exact defect the comment on AllNukes above was written about,
		/// left in place on this one test. The twelve arsenal weapons were checked by nothing.
		///
		/// The second and third assertions are new with the banded scars (2026-09-08). Since the
		/// change every nuclear scar is a set of annuli rather than nested filled discs, and that
		/// introduces two failure modes the ceiling check cannot see:
		///
		///   * an inverted annulus. FindTilesInAnnulus throws ArgumentOutOfRangeException when
		///     maxRange &lt; minRange (Map.cs:1990-1991) — again a hard crash, again unlinted.
		///   * a gap or an overlap between bands. Neither crashes: a gap leaves an unpainted ring
		///     through the middle of the scar and an overlap double-paints one, and both are
		///     silent. The bands are generated by tools/impact-scar/rewire_bands.py, so the way
		///     this breaks is a later hand-edit to one Size.
		/// </summary>
		[Test]
		public void NoSmudgeRadiusCrossesTheTileSearchCeiling()
		{
			const int MaximumTileSearchRange = 56;
			foreach (var (weapon, _kt) in AllNukes)
			{
				var bands = new List<(string Key, int Inner, int Outer)>();
				foreach (var node in Weapon(weapon).Nodes.Where(n => n.Value.Value == "LeaveSmudge"))
				{
					var what = $"{weapon} {node.Key}";

					// `Size` is `outer` for a filled disc or `outer, inner` for an annulus
					// (LeaveSmudgeWarhead.cs:53-54 — minRange is Size[1], maxRange is Size[0]).
					var parts = Field(node.Value, "Size", what).Split(',');
					var outer = int.Parse(parts[0].Trim());
					var inner = parts.Length > 1 ? int.Parse(parts[1].Trim()) : 0;

					Assert.That(outer, Is.LessThanOrEqualTo(MaximumTileSearchRange),
						$"{what} has Size {outer}. FindTilesInAnnulus throws above " +
						$"{MaximumTileSearchRange} and nothing clamps it, so this is a hard crash at the " +
						"tick the smudge lands, with no lint to catch it.");

					Assert.That(inner, Is.LessThanOrEqualTo(outer),
						$"{what} is an inverted annulus (inner {inner} > outer {outer}). " +
						"FindTilesInAnnulus throws ArgumentOutOfRangeException on that, Map.cs:1990-1991.");

					bands.Add((node.Key, inner, outer));
				}

				// Contiguity only means anything for a banded scar; a weapon still drawing one or
				// two plain discs (MOPPenetration-style) is not making this claim.
				if (bands.Count < 2 || !bands.All(b => b.Key.StartsWith("Warhead@Scar", StringComparison.Ordinal)))
					continue;

				var ordered = bands.OrderBy(b => b.Inner).ToArray();
				Assert.That(ordered[0].Inner, Is.Zero, $"{weapon}'s innermost scar band starts at {ordered[0].Inner}, not 0 — the blast centre is unpainted.");
				for (var i = 1; i < ordered.Length; i++)
					Assert.That(ordered[i].Inner, Is.EqualTo(ordered[i - 1].Outer + 1),
						$"{weapon}: {ordered[i].Key} starts at {ordered[i].Inner} but {ordered[i - 1].Key} ends at " +
						$"{ordered[i - 1].Outer}. Bands must tile exactly — a gap is an unpainted ring and an " +
						"overlap double-paints one, and neither throws.");
			}
		}
	}
}
