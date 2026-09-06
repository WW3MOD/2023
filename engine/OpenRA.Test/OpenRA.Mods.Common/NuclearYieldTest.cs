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
		static double FireballTicks(double kt) => 0.21045 * Math.Pow(kt, 0.44) * TicksPerSecond;
		static double ThermalCells(double kt) => 0.5593 * Math.Pow(kt, 0.41) * 1000.0 / MetresPerCell;

		/// <summary>Rankine-Hugoniot: shock Mach number from peak overpressure, at 14.7 psi ambient.</summary>
		static double Mach(double psi) => Math.Sqrt(1 + 6 / 7.0 * psi / 14.7);

		/// <summary>
		/// The wavefront integration ShockwaveEffect.Tick runs, reproduced exactly — same integer
		/// operands, same integer divisions, same order. Every arrival tick quoted in the YAML comes
		/// from this, so if the engine's loop is ever rewritten in floating point the numbers here
		/// stop matching and someone finds out.
		/// </summary>
		static List<int> Wavefront(int startRadius, int maxRadius, int waveSpeed, int initialSpeedPercent, int decayPercent)
		{
			var step = 1024 / waveSpeed;
			var radius = startRadius;
			var excess = (initialSpeedPercent - 100) * 10;
			var history = new List<int> { radius };
			while (radius <= maxRadius && history.Count < 5000)
			{
				radius += step * (1000 + excess) / 1000;
				excess = excess * decayPercent / 100;
				history.Add(radius);
			}

			return history;
		}

		/// <summary>Tick at which the front first reaches <paramref name="cells"/>, plus the start delay.</summary>
		static int ArrivalTick(List<int> history, double cells, int startDelay)
		{
			for (var t = 0; t < history.Count; t++)
				if (history[t] >= cells * 1024)
					return t + startDelay;

			return -1;
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

		static MiniYaml Weapon(string name)
		{
			var node = MiniYaml.FromFile(FindRules("weapons", "weapons-superweapons.yaml"))
				.FirstOrDefault(n => n.Key == name);
			Assert.That(node, Is.Not.Null, $"{name} is not defined in weapons-superweapons.yaml — this test is scanning nothing");
			return node.Value;
		}

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
			foreach (var (weapon, kt, expectedMach) in new[]
			{
				("Atomic", TacticalKt, 5.51),
				("AtomicHighYield", StrategicKt, 4.05),
			})
			{
				var w = Warhead(weapon, "Warhead@BlastWave");
				var startRadius = Cells(Dist(w, "StartRadius", weapon));
				var initialSpeed = Int(w, "InitialSpeedPercent", weapon);

				Assert.That(startRadius, Is.EqualTo(FireballCells(kt)).Within(0.02),
					$"{weapon}'s wave starts at {startRadius}c but its fireball breaks away at {FireballCells(kt):F3}c");

				// The overpressure the blast law puts at the breakaway radius, through Rankine-Hugoniot.
				var mach = Mach(BlastPsi(kt, startRadius));
				Assert.That(mach, Is.EqualTo(expectedMach).Within(0.05));
				Assert.That(initialSpeed, Is.EqualTo((int)Math.Round(mach * 100)).Within(2),
					$"{weapon}'s InitialSpeedPercent {initialSpeed} is not the Mach {mach:F2} its own breakaway overpressure implies");
			}

			// The LARGER weapon's front is born SLOWER, which looks like a mistake and is not: a bigger
			// fireball breaks away at a lower overpressure. If this ever inverts, someone has "fixed" it.
			Assert.That(Int(Warhead("AtomicHighYield", "Warhead@BlastWave"), "InitialSpeedPercent", "strategic"),
				Is.LessThan(Int(Warhead("Atomic", "Warhead@BlastWave"), "InitialSpeedPercent", "tactical")),
				"the 6 Mt front is now born faster than the 20 kt one; breakaway Mach falls with yield, it does not rise");
		}

		/// <summary>
		/// Every suppression Delay in the YAML claims to be the tick the wavefront reaches that band.
		/// This runs the engine's own integer integration and checks that it is.
		/// </summary>
		[Test]
		public void SuppressionDelaysAreTheTicksTheWavefrontActuallyArrives()
		{
			foreach (var weapon in new[] { "Atomic", "AtomicHighYield" })
			{
				var blast = Warhead(weapon, "Warhead@BlastWave");
				var history = Wavefront(
					Dist(blast, "StartRadius", weapon),
					Dist(blast, "MaxRadius", weapon),
					Int(blast, "WaveSpeed", weapon),
					Int(blast, "InitialSpeedPercent", weapon),
					Int(blast, "SpeedDecayPercent", weapon));
				var startDelay = Int(blast, "StartDelay", weapon);
				var maxRadius = Cells(Dist(blast, "MaxRadius", weapon));

				foreach (var band in new[]
				{
					"Warhead@SuppressionClose", "Warhead@SuppressionMedium",
					"Warhead@SuppressionFar", "Warhead@SuppressionWind",
				})
				{
					var b = Warhead(weapon, band);
					var range = Cells(Dist(b, "Range", $"{weapon} {band}"));
					var delay = Int(b, "Delay", $"{weapon} {band}");
					var arrival = ArrivalTick(history, range, startDelay);

					Assert.That(range, Is.LessThanOrEqualTo(maxRadius),
						$"{weapon} {band} sits outside MaxRadius, so the wave never arrives and its Delay is fiction");
					Assert.That(delay, Is.EqualTo(arrival).Within(2),
						$"{weapon} {band} is at {range}c and fires at tick {delay}, but the wavefront gets there at {arrival}");
				}
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

			foreach (var waveSpeed in new[] { 2, 3, 5, 7, 10, 25 })
			{
				var history = Wavefront(0, 200 * 1024, waveSpeed, 100, 100);
				var step = 1024 / waveSpeed;
				for (var t = 0; t < history.Count; t++)
					Assert.That(history[t], Is.EqualTo(t * step),
						$"at WaveSpeed {waveSpeed}, tick {t}: the integrated front is no longer ticks * (1024 / WaveSpeed)");
			}
		}

		/// <summary>
		/// The fireball light. Loads the shipped envelope through the same FieldLoader path the game
		/// uses — so a mismatched keyframe array fails here rather than at mod load — and then checks
		/// the three things that make it a nuclear fireball rather than a generic flash.
		/// </summary>
		[Test]
		public void BothFireballsAreDoubleFlashesThatCoolAndLastAsLongAsTheirYieldSays()
		{
			foreach (var (weapon, kt, secondPeakTick) in new[]
			{
				("Atomic", TacticalKt, 2),
				("AtomicHighYield", StrategicKt, 41),
			})
			{
				var light = LightEventDefinition.LoadFrom(Warhead(weapon, "Warhead@FireballLight"), "Light", true);

				// 1. DURATION is the fireball's own breakaway lifetime, t = 0.2104 * Y^0.44 seconds.
				Assert.That(light.Duration, Is.EqualTo((int)Math.Round(FireballTicks(kt))).Within(1),
					$"{weapon}'s fireball lasts {light.Duration} ticks against the {FireballTicks(kt):F0} its yield implies");

				// 2. THE DOUBLE FLASH: a first maximum, a strictly lower dip, then a strictly larger
				//    second maximum. Any envelope with one peak is not a nuclear fireball.
				var i = light.Intensities;
				var peakIndex = Array.IndexOf(i, i.Max());

				// The dip is the minimum BEFORE the second maximum. Searching the whole array would
				// find the terminal zero every time, which is the envelope ending rather than the
				// shock front going opaque.
				Assert.That(peakIndex, Is.GreaterThan(1), $"{weapon}'s largest maximum is at keyframe {peakIndex}, leaving no room for a dip before it");
				var dipIndex = 1;
				for (var k = 1; k < peakIndex; k++)
					if (i[k] < i[dipIndex])
						dipIndex = k;
				Assert.That(i[0], Is.GreaterThan(i[dipIndex]), $"{weapon} has no first pulse before the dip");
				Assert.That(i[peakIndex], Is.GreaterThan(i[0]), $"{weapon}'s second maximum is not larger than its first");

				// The second maximum lands where t_max = 0.032*sqrt(Y) seconds puts it.
				Assert.That(light.Times[peakIndex], Is.EqualTo(secondPeakTick).Within(1));
				Assert.That(secondPeakTick, Is.EqualTo((int)Math.Round(0.032 * Math.Sqrt(kt) * TicksPerSecond)).Within(1),
					"the second-maximum tick no longer matches the Glasstone double-flash timing");

				// 3. IT COOLS. From the second maximum onward the colour walks white -> yellow ->
				//    orange -> dull red, monotonically.
				//
				//    The measure is the red:blue RATIO and not red MINUS blue, which is the trap here: a
				//    cooling fireball also darkens, so the difference can fall across a step that is
				//    plainly redder (FF9C3C -> B4280A drops R-B from 195 to 170 while R/B climbs from
				//    4.3 to 18). Difference measures brightness as much as hue; ratio measures hue.
				for (var k = peakIndex; k + 1 < light.Tints.Length; k++)
				{
					var a = light.Tints[k];
					var b = light.Tints[k + 1];
					Assert.That(b.B, Is.LessThanOrEqualTo(a.B), $"{weapon}'s fireball gets BLUER between keyframes {k} and {k + 1}");
					Assert.That((b.R + 1f) / (b.B + 1f), Is.GreaterThanOrEqualTo((a.R + 1f) / (a.B + 1f)),
						$"{weapon}'s fireball stops warming toward red at keyframe {k + 1}");
				}

				var final = light.Tints[light.Tints.Length - 1];
				Assert.That(final.R, Is.GreaterThan(2 * final.B), $"{weapon}'s fireball does not end on a dull red");

				// The light's maximum radius is the thermal radius: illumination reach scales as the
				// fireball radius (Y^0.40) and thermal as Y^0.41, close enough that one array carries both.
				Assert.That(Cells(light.MaximumRadius.Length), Is.EqualTo(ThermalCells(kt)).Within(3.0),
					$"{weapon}'s light no longer reaches its thermal radius");
			}
		}

		/// <summary>
		/// Yield buys AREA and DURATION, not brightness. A fireball's surface is ~7000 K at the second
		/// maximum whatever set it off, so the peak intensities must match; making the strategic weapon
		/// brighter instead of bigger is the one obvious wrong way to scale this.
		/// </summary>
		[Test]
		public void YieldScalesTheFireballsSizeAndLifetimeButNotItsBrightness()
		{
			var tactical = LightEventDefinition.LoadFrom(Warhead("Atomic", "Warhead@FireballLight"), "Light", true);
			var strategic = LightEventDefinition.LoadFrom(Warhead("AtomicHighYield", "Warhead@FireballLight"), "Light", true);

			Assert.That(strategic.Intensities.Max(), Is.EqualTo(tactical.Intensities.Max()).Within(0.01),
				"the two fireballs no longer peak at the same brightness; surface temperature does not scale with yield");

			var radiusRatio = (double)strategic.MaximumRadius.Length / tactical.MaximumRadius.Length;
			var lifetimeRatio = (double)strategic.Duration / tactical.Duration;
			Assert.That(radiusRatio, Is.EqualTo(Math.Pow(StrategicKt / TacticalKt, 0.41)).Within(1.0));
			Assert.That(lifetimeRatio, Is.EqualTo(Math.Pow(StrategicKt / TacticalKt, 0.44)).Within(1.5));
		}

		/// <summary>
		/// LeaveSmudgeWarhead reaches Map.FindTilesInAnnulus, which THROWS above
		/// MapGrid.MaximumTileSearchRange (56) rather than clamping. There is no lint for it, and the
		/// failure is a hard crash on the tick the smudge lands — the same ceiling that killed the game
		/// through CameraRange on 2026-09-06. AtomicHighYield's thermal radius is 124 cells and the
		/// temptation to scorch that far is real.
		/// </summary>
		[Test]
		public void NoSmudgeRadiusCrossesTheTileSearchCeiling()
		{
			const int MaximumTileSearchRange = 56;
			foreach (var weapon in new[] { "Atomic", "AtomicHighYield" })
			{
				foreach (var node in Weapon(weapon).Nodes.Where(n => n.Value.Value == "LeaveSmudge"))
				{
					var size = int.Parse(Field(node.Value, "Size", $"{weapon} {node.Key}"));
					Assert.That(size, Is.LessThanOrEqualTo(MaximumTileSearchRange),
						$"{weapon} {node.Key} has Size {size}. FindTilesInAnnulus throws above " +
						$"{MaximumTileSearchRange} and nothing clamps it, so this is a hard crash at the " +
						"tick the smudge lands, with no lint to catch it.");
				}
			}
		}
	}
}
