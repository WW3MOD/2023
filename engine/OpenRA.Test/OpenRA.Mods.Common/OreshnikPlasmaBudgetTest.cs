#region Copyright & License Information
/*
 * WW3MOD -- the Oreshnik's speed against the two ceilings that silently switch its plasma off.
 *
 * THE FAILURE THIS EXISTS TO CATCH IS A SILENT ONE AND IT HAS NO OTHER GATE. WithHypersonicPlasma
 * and SubTickMotionSmoothing each carry a MaxStep, and each treats a per-tick step longer than its
 * own as a TELEPORT rather than as motion:
 *
 *     velocity = step.LengthSquared > maxStepSquared ? WVec.Zero : step;
 *
 * A zero velocity makes both traits return the actor's renderables unmodified. So a missile over
 * the ceiling does not look degraded -- it loses the ENTIRE effect, every tick, with no exception,
 * no lint error and no log line. Nothing in the build, the YAML lint or any other test reads these
 * two numbers together, and the mistake is invisible in a diff because the speed and the ceiling
 * live in different traits and often in different files.
 *
 * THE TRAP IS THAT MaxStep IS MEASURED AGAINST THE 3D STEP AND `Speed` IS HORIZONTAL ONLY.
 * BallisticMissile.Speed caps horizontal closing speed; BallisticMissileFly then adds a parabolic
 * arc on top (BallisticMissileFly.cs:396-406), so the distance the actor actually moves in a tick
 * is Speed * sqrt(1 + slope^2). A steep LaunchAngle can therefore put a missile over the ceiling at
 * a Speed comfortably under it -- which is exactly the shape of the Oreshnik, and is why the
 * ^ShootableMissile default of 6144 was not enough for it despite 3600 being well below 6144.
 *
 * WHERE THE SLOPE COMES FROM, and it is worth stating because it is what makes this checkable at
 * all without a World. The arc is parabolic with peak = hDist * tan(LaunchAngle) / 4 and height
 * 4 * peak * p * (1 - p), so d(height)/d(horizontal) is tan(LaunchAngle) * (1 - 2p): +tan at
 * launch, -tan at impact, INDEPENDENT OF hDist. SpawnAltitude adds a further constant
 * SpawnAltitude / standoff, because baseZ is interpolated linearly from spawn to target
 * (BallisticMissileFly.cs:402). The worst case is therefore the SHORTEST standoff in the mod, which
 * is the smallest map -- arena-tank-duel at 66x34.
 *
 * SCOPE. Arithmetic over the shipped YAML: no World, no mod load, no launch slot. It proves the
 * ceilings clear the speed and that the Oreshnik really is the fastest thing in the mod. It does
 * NOT prove anything is drawn -- that is what tools/autotest/scenarios/demo-oreshnik-plasma is for.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class OreshnikPlasmaBudgetTest
	{
		/// <summary>Smallest shipped map, and therefore the shortest standoff and the steepest baseZ.</summary>
		const int SmallestMapCellsX = 66;
		const int SmallestMapCellsY = 34;

		/// <summary><see cref="OpenRA.Mods.Common.Traits.MissileStrikePowerInfo"/>'s ApproachMargin default.</summary>
		const int ApproachMargin = 16 * 1024;

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

		/// <summary>One actor's flight and render numbers, read straight out of the shipped rules.</summary>
		sealed class MissileProfile
		{
			public string Actor;
			public string File;
			public int Speed;
			public int TerminalSpeed;
			public int LaunchAngle;
			public int SmoothingMaxStep;
			public int PlasmaMaxStep;
			public bool HasPlasma;

			/// <summary>The faster of cruise and terminal -- what the missile is doing when it matters.</summary>
			public int TopHorizontalSpeed => Math.Max(Speed, TerminalSpeed);
		}

		// A hand-rolled walk rather than a Ruleset load, for the same reason PowerPurchaseWiringTest
		// walks the files: standing up a ModData here would need a real mod load, and the numbers
		// wanted are all plain integers sitting at a known depth. Inheritance is NOT resolved, so a
		// value that comes from ^ShootableMissile is read as absent and defaulted below -- which is
		// correct for every field used here, because the template's own values are the defaults.
		static readonly Regex ActorKey = new(@"^([A-Za-z_][\w.]*):", RegexOptions.Compiled);
		static readonly Regex TraitKey = new(@"^\t([A-Za-z][\w]*)(@[\w.]+)?:", RegexOptions.Compiled);
		static readonly Regex FieldKey = new(@"^\t\t([A-Za-z][\w]*):\s*([^#]*)", RegexOptions.Compiled);

		/// <summary>WDist YAML: a bare number, or `AcB` meaning A cells and B units.</summary>
		static int ParseWDist(string raw)
		{
			var s = raw.Trim();
			var c = s.IndexOf('c');
			if (c < 0)
				return int.Parse(s, CultureInfo.InvariantCulture);

			return (int.Parse(s[..c], CultureInfo.InvariantCulture) * 1024)
				+ int.Parse(s[(c + 1)..], CultureInfo.InvariantCulture);
		}

		static List<MissileProfile> Profiles()
		{
			var found = new List<MissileProfile>();

			foreach (var file in Directory.EnumerateFiles(
				Path.GetDirectoryName(FindMod("rules", "defaults.yaml")), "*.yaml", SearchOption.AllDirectories))
			{
				MissileProfile current = null;
				var trait = string.Empty;

				foreach (var line in File.ReadLines(file))
				{
					if (line.Length == 0 || line[0] == '#')
						continue;

					var actor = ActorKey.Match(line);
					if (actor.Success)
					{
						if (current != null && current.Speed > 0)
							found.Add(current);

						current = new MissileProfile { Actor = actor.Groups[1].Value, File = Path.GetFileName(file) };
						trait = string.Empty;
						continue;
					}

					if (current == null)
						continue;

					var t = TraitKey.Match(line);
					if (t.Success)
					{
						trait = t.Groups[1].Value;
						if (trait == "WithHypersonicPlasma")
							current.HasPlasma = true;

						continue;
					}

					var f = FieldKey.Match(line);
					if (!f.Success)
						continue;

					var key = f.Groups[1].Value;
					var value = f.Groups[2].Value.Trim();
					if (value.Length == 0)
						continue;

					switch (trait)
					{
						case "BallisticMissile":
							if (key == "Speed") current.Speed = int.Parse(value, CultureInfo.InvariantCulture);
							else if (key == "TerminalSpeed") current.TerminalSpeed = int.Parse(value, CultureInfo.InvariantCulture);
							else if (key == "LaunchAngle") current.LaunchAngle = int.Parse(value, CultureInfo.InvariantCulture);
							break;
						case "SubTickMotionSmoothing":
							if (key == "MaxStep") current.SmoothingMaxStep = ParseWDist(value);
							break;
						case "WithHypersonicPlasma":
							if (key == "MaxStep") current.PlasmaMaxStep = ParseWDist(value);
							break;
					}
				}

				if (current != null && current.Speed > 0)
					found.Add(current);
			}

			// The abstract template is not an actor and its 110/128 are placeholders nothing flies at.
			return found.Where(p => !p.Actor.StartsWith('^')).ToList();
		}

		static MissileProfile Oreshnik()
		{
			var p = Profiles().SingleOrDefault(x => x.Actor == "OreshnikRV");
			Assert.That(p, Is.Not.Null, "OreshnikRV not found in mods/ww3mod/rules -- has it been renamed?");
			return p;
		}

		/// <summary>tan(LaunchAngle), as WAngle.Tan returns it: the true tangent times 1024.</summary>
		static int TanScaled(int launchAngleUnits)
		{
			return new WAngle(launchAngleUnits).Tan();
		}

		/// <summary>
		/// Worst-case distance the actor moves in one tick, over every shipped map.
		/// </summary>
		/// <remarks>
		/// Horizontal component is the top speed; vertical is that times the steepest slope the
		/// flight reaches, which is the arc's own tan(LaunchAngle) plus the constant baseZ slope
		/// SpawnAltitude/standoff. Computed in double here purely because this is a test asserting a
		/// margin -- nothing on the simulation path does floating-point with these numbers.
		/// </remarks>
		static double WorstStep(MissileProfile p, int spawnAltitude)
		{
			var diagonal = Math.Sqrt(
				Math.Pow(1024.0 * SmallestMapCellsX, 2) + Math.Pow(1024.0 * SmallestMapCellsY, 2));
			var standoff = diagonal + ApproachMargin;

			var slope = (TanScaled(p.LaunchAngle) / 1024.0) + (spawnAltitude / standoff);
			return p.TopHorizontalSpeed * Math.Sqrt(1 + (slope * slope));
		}

		[Test]
		public void TheFixtureFindsTheMissilesItIsCheckingRatherThanAnEmptyList()
		{
			var all = Profiles();

			// If the walk ever silently stops matching, every other test here passes vacuously.
			Assert.That(all.Count, Is.GreaterThanOrEqualTo(15),
				"the rules walk found only " + all.Count + " BallisticMissile actors; it used to find " +
				"twenty. The YAML shape it depends on -- an actor at column 0, a trait at one tab, a " +
				"field at two -- has probably changed.");

			Assert.That(all.Select(p => p.Actor), Contains.Item("KinzhalMissile"));
			Assert.That(all.Select(p => p.Actor), Contains.Item("OreshnikRV"));
		}

		[Test]
		public void TheOreshnikIsTheFastestThingInTheMod()
		{
			var all = Profiles();
			var oreshnik = Oreshnik();

			var runnerUp = all
				.Where(p => p.Actor != oreshnik.Actor)
				.OrderByDescending(p => p.TopHorizontalSpeed)
				.First();

			// The user's ask was "the fastest moving thing in the entire game", so this is the claim
			// being pinned rather than a nice-to-have. Stated on the HORIZONTAL axis because that is
			// the axis `Speed` is written on and the one another author will compare against; the
			// through-the-air figure is larger still, because this missile is much steeper.
			Assert.That(oreshnik.TopHorizontalSpeed, Is.GreaterThan(runnerUp.TopHorizontalSpeed),
				$"{runnerUp.Actor} ({runnerUp.File}) now tops out at {runnerUp.TopHorizontalSpeed} " +
				$"wdist/tick against the Oreshnik's {oreshnik.TopHorizontalSpeed}. Either raise the " +
				"Oreshnik or stop describing it as the fastest thing in the game -- the claim is " +
				"written into its Description, its cameo text and its comments.");
		}

		[Test]
		public void BothMaxStepCeilingsClearTheWorstTickTheOreshnikCanFly()
		{
			var p = Oreshnik();

			// SpawnAltitude lives on the POWER, not the actor. Read from the constant here rather
			// than parsed, and pinned by TheSpawnAltitudeThisTestAssumesIsWhatThePowerShips below, so
			// that changing one without the other fails loudly instead of invalidating this margin.
			const int SpawnAltitude = 24 * 1024;
			var worst = WorstStep(p, SpawnAltitude);

			Assert.That(p.SmoothingMaxStep, Is.GreaterThan(0),
				"OreshnikRV does not override SubTickMotionSmoothing.MaxStep, so it inherits the " +
				"6144 default from ^ShootableMissile -- which this missile exceeds. It will render " +
				"unsmoothed, with no error anywhere.");

			Assert.That(p.PlasmaMaxStep, Is.GreaterThan(0),
				"OreshnikRV does not override WithHypersonicPlasma.MaxStep. The two traits keep " +
				"SEPARATE ceilings and raising one does not raise the other; at the 6144 default " +
				"this missile draws no plasma at all for its entire flight.");

			Assert.That(p.SmoothingMaxStep, Is.GreaterThan(worst),
				$"the steepest tick is {worst:F0} wdist against a SubTickMotionSmoothing ceiling of " +
				$"{p.SmoothingMaxStep}. Over the ceiling the trait produces NOTHING -- this is a total " +
				"loss of smoothing, not a degradation.");

			Assert.That(p.PlasmaMaxStep, Is.GreaterThan(worst),
				$"the steepest tick is {worst:F0} wdist against a WithHypersonicPlasma ceiling of " +
				$"{p.PlasmaMaxStep}. Over the ceiling there is no sheath, no wake and no body tint, " +
				"on every tick, silently.");
		}

		[Test]
		public void TheDefaultCeilingWouldNotHaveBeenEnough()
		{
			// The reason the two overrides above exist, asserted rather than left to a comment: if
			// this ever stops being true the overrides are dead weight and should go, and if the
			// margin above ever gets tuned down toward 6144 this is what says why it cannot.
			var worst = WorstStep(Oreshnik(), 24 * 1024);

			Assert.That(worst, Is.GreaterThan(6144),
				"the Oreshnik no longer exceeds the shipped 6144 default, so the MaxStep overrides " +
				"on OreshnikRV are no longer load-bearing. Either the speed or the launch angle has " +
				"been reduced; check that the weapon is still meant to be the fastest in the mod.");
		}

		[Test]
		public void TheSpawnAltitudeThisTestAssumesIsWhatThePowerShips()
		{
			var player = MiniYaml.FromFile(FindMod("rules", "player.yaml"));

			static MiniYamlNode Find(IEnumerable<MiniYamlNode> nodes)
			{
				foreach (var n in nodes)
				{
					if (n.Key == "MissileStrikePower@Oreshnik")
						return n;

					var inner = Find(n.Value.Nodes);
					if (inner != null)
						return inner;
				}

				return null;
			}

			var power = Find(player)
				?? throw new AssertionException("MissileStrikePower@Oreshnik not found in rules/player.yaml");

			var altitude = power.Value.Nodes.FirstOrDefault(n => n.Key == "SpawnAltitude")?.Value.Value?.Trim();
			Assert.That(altitude, Is.Not.Null, "MissileStrikePower@Oreshnik has no SpawnAltitude");
			Assert.That(ParseWDist(altitude), Is.EqualTo(24 * 1024),
				"SpawnAltitude has moved. It feeds the baseZ slope, which feeds the worst-case tick " +
				"BothMaxStepCeilingsClearTheWorstTickTheOreshnikCanFly is sized against -- update the " +
				"constant there in the same edit.");

			// The missile named by the power has to be the one this whole fixture measures, or every
			// number above is about an actor that never flies.
            var actor = power.Value.Nodes.FirstOrDefault(n => n.Key == "MissileActor")?.Value.Value?.Trim();
			Assert.That(actor, Is.EqualTo("oreshnikrv"),
				"MissileStrikePower@Oreshnik flies a different actor than the one measured here.");
		}

		[Test]
		public void TheLeadingSheathSurvivesTwoTicksOutAndIsGoneOneTickOut()
		{
			// THE DOCUMENTED COST OF THE IMPACT CLAMP, pinned so the next author does not read the
			// missing bloom on the final frames as a bug and "fix" it by weakening the clamp.
			//
			// LeadingSamplesWithin reserves a whole tick of travel before spending anything on
			// samples, because SubTickMotionSmoothing may already have drawn the body that far ahead
			// and the two offsets add. Ticks are discrete, so `remaining` steps by a whole tick at a
			// time and the budget is either a full tick's worth or nothing -- it never lands in the
			// range where the sheath would shorten gracefully. That is true at any speed; going
			// faster does not cost more TICKS of bloom, it makes the one lost tick span more ground.
			var p = Oreshnik();
			var step = new WDist((int)WorstStep(p, 24 * 1024));
			var spacing = new WDist(160);
			const int Samples = 8;

			var twoOut = OpenRA.Mods.Common.Traits.Render.WithHypersonicPlasmaMath.LeadingSamplesWithin(
				Samples, spacing, new WDist(2 * step.Length), step);
			Assert.That(twoOut, Is.EqualTo(Samples),
				"two ticks from impact the whole sheath must still be drawn; if this drops, the " +
				"bloom is absent for more than the one unavoidable tick and the effect is being " +
				"eaten by the clamp rather than merely clipped by it.");

			var oneOut = OpenRA.Mods.Common.Traits.Render.WithHypersonicPlasmaMath.LeadingSamplesWithin(
				Samples, spacing, step, step);
			Assert.That(oneOut, Is.Zero,
				"one tick from impact there is no room ahead of the nose and nothing may be drawn " +
				"there. This is TheSheathIsClearOfTheEndpointBeforeArrivalNotOnlyAtIt's invariant " +
				"seen from the Oreshnik's speed.");
		}
	}
}
