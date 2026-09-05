#region Copyright & License Information
/*
 * WW3MOD missile-strike ARRIVAL — the three things that changed about how a MissileStrikePower
 * reaches its aim point, pinned as arithmetic and YAML reads. No World, no Actor, no game run.
 *
 * The load-bearing discovery is in the first section, and it is not obvious from any Desc: whether
 * a detonation's ALTITUDE costs it damage depends entirely on the victim's HITSHAPE TYPE.
 * Circle and Polygon measure DistanceFromEdge in three dimensions; Rectangle and Capsule discard
 * the Z component and return a horizontal distance. So the same airburst is free against every
 * vehicle and building in the mod (Rectangle) and fully discounted against infantry (Circle).
 * That asymmetry is the whole balance story of the tactical nuke's airburst, it is exactly
 * backwards from the physics it is imitating, and nothing in the engine says so out loud.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Test
{
	[TestFixture]
	public class MissileStrikeArrivalTest
	{
		// player.yaml, MissileStrikePower@TacNuke. 6c256 == mslo's NukePower DetonationAltitude.
		const int TacNukeBurstAltitude = 6400;

		// Every Atomic warhead writes AirThreshold: 10c0. Above it the engine substitutes `Air` for
		// the terrain target types and the warhead does nothing to the ground (Warhead.cs:41-45).
		const int AtomicAirThreshold = 10 * 1024;

		// The 8 cells of descent the Kinzhal has and the tac nuke must keep: SpawnAltitude minus
		// DetonationAltitude, because BallisticMissileFly interpolates baseZ to the TARGET Z.
		const int RequiredDescent = 8192;

		// --- (1) the vertical leg is shape-dependent ------------------------------------------

		static RectangleShape VehicleShape()
		{
			// A T-90-shaped hull. Vehicles in this mod carry VerticalTopOffset 480; it is left at
			// the default here because Rectangle discards Z either way, which is the point.
			var s = new RectangleShape(new int2(-290, -770), new int2(290, 770));
			s.Initialize();
			return s;
		}

		static CircleShape InfantryShape()
		{
			// infantry.yaml HitShape@Standing: Type Circle, Radius 30.
			var s = new CircleShape(new WDist(30));
			s.Initialize();
			return s;
		}

		[Test]
		public void RectangleHitShapesIgnoreDetonationAltitudeEntirely()
		{
			var shape = VehicleShape();
			var victim = new WPos(20480, 20480, 0);

			var ground = shape.DistanceFromEdge(victim, victim, WRot.None).Length;
			var burst = shape.DistanceFromEdge(victim + new WVec(0, 0, TacNukeBurstAltitude), victim, WRot.None).Length;

			// Rectangle.DistanceFromEdge builds its result vector with a hardcoded Z of 0 and then
			// returns HorizontalLength (Rectangle.cs:109-116). The 6400 is discarded.
			Assert.That(ground, Is.EqualTo(0));
			Assert.That(burst, Is.EqualTo(0),
				"a Rectangle hitshape discards the vertical leg, so an airburst over a vehicle is " +
				"measured as a direct hit -- the tac nuke's airburst costs it NOTHING against " +
				"vehicles or buildings");
		}

		[Test]
		public void CircleHitShapesPayTheFullVerticalLeg()
		{
			var shape = InfantryShape();
			var victim = new WPos(20480, 20480, 0);

			var ground = shape.DistanceFromEdge(victim, victim, WRot.None).Length;
			var burst = shape.DistanceFromEdge(victim + new WVec(0, 0, TacNukeBurstAltitude), victim, WRot.None).Length;

			// Circle.DistanceFromEdge is `v.Length - Radius` on the full 3-D vector (Circle.cs:46-48).
			Assert.That(ground, Is.EqualTo(0));
			Assert.That(burst, Is.EqualTo(TacNukeBurstAltitude - 30),
				"a Circle hitshape measures the burst in three dimensions, so infantry directly " +
				"under the airburst are treated as 6.2 cells away");
		}

		// --- (2) what that costs the Atomic warhead -------------------------------------------

		// weapons-superweapons.yaml, Warhead@ThermalVaporize: Spread 3c0, Falloff 100,100,100,50.
		static readonly int[] VaporizeFalloff = { 100, 100, 100, 50 };
		static WDist[] VaporizeRanges => Exts.MakeArray(VaporizeFalloff.Length, i => new WDist(i * 3072));

		// weapons-superweapons.yaml, Warhead@ThermalRadiation: Spread 1c0, 15 falloff steps.
		static readonly int[] ThermalFalloff = { 100, 60, 35, 20, 12, 8, 5, 3, 2, 1, 1, 0, 0, 0, 0 };
		static WDist[] ThermalRanges => Exts.MakeArray(ThermalFalloff.Length, i => new WDist(i * 1024));

		static int InfantryBurstDistance()
		{
			return InfantryShape().DistanceFromEdge(
				new WPos(20480, 20480, TacNukeBurstAltitude), new WPos(20480, 20480, 0), WRot.None).Length;
		}

		[Test]
		public void TheVaporizeBandStillKillsInfantryUnderTheAirburst()
		{
			var d = InfantryBurstDistance();
			var pct = SpreadDamageWarhead.DamageFalloff(d, VaporizeFalloff, VaporizeRanges);

			// The Falloff table is FLAT at 100 out to its third step (6144), which is only 226 short
			// of the burst distance -- so the innermost band barely notices the altitude. This is
			// why the airburst does not turn the nuke into a firework.
			Assert.That(pct, Is.GreaterThan(90),
				$"ThermalVaporize retains {pct}% at the airburst distance {d}");
			Assert.That(200000 * pct / 100, Is.GreaterThan(1000),
				"200000 at >90% still vaporises any infantry in the mod");
		}

		[Test]
		public void TheSustainedThermalBandIsWhatTheAirburstActuallyCosts()
		{
			var d = InfantryBurstDistance();
			var atGround = SpreadDamageWarhead.DamageFalloff(0, ThermalFalloff, ThermalRanges);
			var atBurst = SpreadDamageWarhead.DamageFalloff(d, ThermalFalloff, ThermalRanges);

			Assert.That(atGround, Is.EqualTo(100));
			Assert.That(atBurst, Is.LessThan(10),
				$"ThermalRadiation's 1-cell falloff collapses to {atBurst}% at the airburst " +
				"distance -- this ONE warhead is the whole cost of bursting at 6c256, and it is " +
				"paid only by Circle-hitshape victims (infantry)");
			Assert.That(atGround - atBurst, Is.GreaterThan(0));
		}

		[Test]
		public void ConditionWarheadsAreIndifferentToBurstAltitude()
		{
			// GrantExternalConditionWarhead does FindActorsInCircle(target, Range) with no falloff
			// and no shape query (GrantExternalConditionWarhead.cs:60-61), and FindActorsInCircle is
			// a horizontal cell search. So Atomic's ten fire stacks, its EMP and all five
			// suppression bands reach exactly as far from an airburst as from a ground burst.
			// There is no arithmetic to assert here; this test exists to fail loudly if someone
			// ever gives that path a vertical term and quietly changes the nuke's fire radius.
			var horizontalOnly = new WVec(4096, 0, TacNukeBurstAltitude);
			Assert.That(horizontalOnly.HorizontalLength, Is.EqualTo(4096),
				"HorizontalLength must stay Z-blind for the fire/EMP/suppression reach argument to hold");
		}

		// --- (3) YAML pins --------------------------------------------------------------------

		static string FindModRules()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules");
				if (Directory.Exists(candidate))
					return candidate;

				dir = dir.Parent;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/rules");
		}

		/// <summary>
		/// Flat read of the child keys of one trait block inside one top-level actor block.
		/// Comment- and blank-line tolerant; deliberately not a MiniYaml parser, because pulling the
		/// real ruleset in would drag the whole mod load into a unit test.
		/// </summary>
		static Dictionary<string, string> ReadTrait(string file, string topLevel, string trait)
		{
			var fields = new Dictionary<string, string>();
			var inTop = false;
			var inTrait = false;
			var seenTop = false;

			foreach (var raw in File.ReadLines(Path.Combine(FindModRules(), file)))
			{
				var line = raw.Split('#')[0].TrimEnd();
				if (line.Trim().Length == 0)
					continue;

				var indent = line.TakeWhile(c => c == '\t').Count();
				var body = line.Trim();

				if (indent == 0)
				{
					if (inTop)
						break;

					inTop = body == topLevel + ":";
					seenTop |= inTop;
					inTrait = false;
				}
				else if (indent == 1 && inTop)
					inTrait = body == trait + ":";
				else if (indent >= 2 && inTrait && body.Contains(':'))
				{
					var parts = body.Split(new[] { ':' }, 2);
					fields[parts[0].Trim()] = parts[1].Trim();
				}
			}

			Assert.That(seenTop, Is.True, $"{topLevel} not found in {file}");
			return fields;
		}

		static int ParseWDist(string v)
		{
			var i = v.IndexOf('c');
			return i < 0 ? int.Parse(v) : (int.Parse(v.Substring(0, i)) * 1024) + int.Parse(v.Substring(i + 1));
		}

		[Test]
		public void TheTwoAtomicDeliveriesBurstAtTheSameHeight()
		{
			var silo = ReadTrait(Path.Combine("ingame", "structures-defenses.yaml"), "MSLO", "NukePower");
			var power = ReadTrait("player.yaml", "Player", "MissileStrikePower@TacNuke");

			Assert.That(silo.ContainsKey("DetonationAltitude"), Is.True);
			Assert.That(power.ContainsKey("DetonationAltitude"), Is.True,
				"the tactical nuclear strike must airburst: it fires the same `Atomic` warhead as " +
				"mslo, and the proposal's balance anchor states that warhead's numbers as a " +
				"6.25-cell airburst");

			var siloAlt = ParseWDist(silo["DetonationAltitude"]);
			var powerAlt = ParseWDist(power["DetonationAltitude"]);

			Assert.That(powerAlt, Is.EqualTo(TacNukeBurstAltitude));
			Assert.That(powerAlt, Is.EqualTo(siloAlt),
				"one warhead, two deliveries, one burst height -- if these diverge the same nuke " +
				"does different damage depending on which menu it came from");
		}

		[Test]
		public void TheAirburstStaysUnderEveryAtomicWarheadsAirThreshold()
		{
			var power = ReadTrait("player.yaml", "Player", "MissileStrikePower@TacNuke");
			var alt = ParseWDist(power["DetonationAltitude"]);

			Assert.That(alt, Is.LessThan(AtomicAirThreshold),
				"above a warhead's AirThreshold the engine swaps the terrain target types for `Air` " +
				"(Warhead.cs:41-45). Atomic writes AirThreshold: 10c0 on every damage, fire, EMP, " +
				"suppression and smudge row, so a burst at 10241 would fly, be aimed, be announced " +
				"-- and do nothing at all, with no error and no lint");

			// The whole Atomic block must keep carrying that threshold, or the pin above is empty.
			var superweapons = File.ReadAllLines(Path.Combine(FindModRules(), "weapons", "weapons-superweapons.yaml"));
			var thresholds = superweapons
				.Select(l => l.Split('#')[0].Trim())
				.Where(l => l.StartsWith("AirThreshold:", StringComparison.Ordinal))
				.Select(l => ParseWDist(l.Split(new[] { ':' }, 2)[1].Trim()))
				.ToArray();

			Assert.That(thresholds.Length, Is.GreaterThan(20),
				"Atomic's warheads used to carry ~30 AirThreshold rows; far fewer means the set " +
				"was rewritten and the airburst needs re-checking");
			Assert.That(thresholds.All(t => t > TacNukeBurstAltitude), Is.True,
				"every AirThreshold in the superweapon file must clear the burst height");
		}

		[Test]
		public void RaisingTheBurstDidNotEatTheDescent()
		{
			var power = ReadTrait("player.yaml", "Player", "MissileStrikePower@TacNuke");
			var spawn = ParseWDist(power["SpawnAltitude"]);
			var burst = ParseWDist(power["DetonationAltitude"]);

			Assert.That(spawn - burst, Is.EqualTo(RequiredDescent),
				"BallisticMissileFly interpolates its base Z from spawn to TARGET (cs:252), so the " +
				"visible descent is SpawnAltitude minus DetonationAltitude. Leaving SpawnAltitude " +
				"at 8c0 would have flattened an 8-cell dive to 1.75 cells");
		}

		[TestCase("MissileStrikePower@Kinzhal")]
		[TestCase("MissileStrikePower@GBU57")]
		public void TheConventionalStrikesMustNotAirburst(string trait)
		{
			var power = ReadTrait("player.yaml", "Player", trait);

			// This is not a taste ruling. Warhead.ValidTargets defaults to `Ground, Water` per
			// warhead and AirThreshold to 128 -- an eighth of a cell. ^HugeExplosionEffects, which
			// both IskanderExplosion and MOPPenetration inherit their visuals from, writes
			// `ValidTargets: Ground, Ship, Trees, Mine` on every CreateEffect row and never `Air`.
			// So a detonation more than 128 units up makes CreateEffectWarhead.IsValidAgainstTerrain
			// test `Air` against a list that lacks it, and DoImpact returns before spawning anything:
			// no explosion sprite, no impact sound, no crater. Atomic is the ONE warhead in the mod
			// written for an airburst, and it says so with an explicit `Air` on its Fireball.
			Assert.That(power.ContainsKey("DetonationAltitude"), Is.False,
				$"{trait} must not carry DetonationAltitude: its effect warheads are not Air-valid, " +
				"so an airburst would detonate silently and invisibly");
		}

		[Test]
		public void TheThreeStrikesArriveOnDistinctSchedules()
		{
			var kinzhal = ReadTrait("player.yaml", "Player", "MissileStrikePower@Kinzhal");
			var gbu = ReadTrait("player.yaml", "Player", "MissileStrikePower@GBU57");
			var nuke = ReadTrait("player.yaml", "Player", "MissileStrikePower@TacNuke");

			var k = int.Parse(kinzhal["MissileDelay"]);
			var g = int.Parse(gbu["MissileDelay"]);
			var n = int.Parse(nuke["MissileDelay"]);

			Assert.That(k, Is.GreaterThan(0), "no strike may enter the map on the tick it is ordered");
			Assert.That(k, Is.LessThan(g), "the hypersonic strike must lead the bomber-delivered one");
			Assert.That(g, Is.LessThan(n), "the nuclear release must be the longest wait of the three");

			// Timestep 60 => 16.667 ticks/s, so seconds = ticks * 6 / 100. NOT 25 tps: that figure,
			// asserted by several in-tree comments, understates every duration by 1.5x.
			Assert.That(n * 6 / 100, Is.EqualTo(30),
				"the nuke's wait is the 30 s the user named, which was a ceiling for all three");
			Assert.That(g * 6 / 100, Is.LessThanOrEqualTo(30));
			Assert.That(k * 6 / 100, Is.LessThanOrEqualTo(30));
		}

		[Test]
		public void OnlyTheNukeAnnouncesItsCountdownToOtherPlayers()
		{
			var kinzhal = ReadTrait("player.yaml", "Player", "MissileStrikePower@Kinzhal");
			var gbu = ReadTrait("player.yaml", "Player", "MissileStrikePower@GBU57");
			var nuke = ReadTrait("player.yaml", "Player", "MissileStrikePower@TacNuke");

			// SupportPowerTimerWidget.Candidates drops a power whose DisplayTimerRelationships is
			// None before it ever asks who is watching, so None removes the line for everybody --
			// including the owner, who keeps the mm:ss printed on the cameo itself
			// (SupportPowersWidget.cs:246).
			Assert.That(kinzhal["DisplayTimerRelationships"], Is.EqualTo("None"));
			Assert.That(gbu["DisplayTimerRelationships"], Is.EqualTo("None"));
			Assert.That(nuke["DisplayTimerRelationships"], Is.EqualTo("Ally, Neutral, Enemy"),
				"the nuke keeps its public clock: a deterrent nobody can see the timer on is not a " +
				"deterrent, and the user named nukes as the exception");
		}
	}
}
