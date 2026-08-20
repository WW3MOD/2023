#region Copyright & License Information
/*
 * WW3MOD dropped-crate auto-target exclusion — corpus pin.
 *
 * A SUPPLYCACHE dropped on the ground must NEVER be acquired by AutoTarget, while remaining
 * force-fireable by an explicit player order. Reported from live play 2026-08-20: "Units fire at
 * supply caches/crates dropped by the enemy. They should not, ever, autotarget them. We have to
 * manually attack them if we want to."
 *
 * This deliberately REVERSES 092db848, which removed NoAutoTarget to give the crate "truck parity"
 * (enemies auto-engage a dropped crate like the truck it came from). That parity is what the player
 * is now reporting as the bug: a crate is loot, not a threat, and shooting it destroys the resources
 * both sides would rather capture.
 *
 * The exclusion lives on the CRATE (a target type) rather than on the weapon (Armament.RequiresForceFire)
 * or in AutoTarget's skip list, because it must hold for every weapon and every unit with no code
 * change. `NoAutoTarget` is read in exactly ONE place engine-wide — AutoTargetPriorityInfo.InvalidTargets
 * (AutoTargetPriority.cs:27) — so it suppresses band resolution without touching weapon validity. That
 * split is the whole point, and it is the trap this file exists to pin: an exclusion implemented as
 * UNTARGETABILITY (dropping Ground/Structure, or a Targetable the weapon cannot match) would also break
 * the manual order the player explicitly asked to keep.
 *
 * Reads the shipped YAML rather than a fixture: the thing being protected is the corpus.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class CrateAutoTargetExclusionTest
	{
		const string Exclusion = "NoAutoTarget";

		static string FindMiscRules()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules", "misc.yaml");
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/misc.yaml");
		}

		/// <summary>The shipped SUPPLYCACHE Targetable.TargetTypes, verbatim.</summary>
		static string[] CacheTargetTypes()
		{
			var cache = MiniYaml.FromFile(FindMiscRules()).FirstOrDefault(n => n.Key == "SUPPLYCACHE");
			Assert.That(cache, Is.Not.Null, "SUPPLYCACHE not found in misc.yaml — this test is scanning nothing");

			var targetTypes = cache.Value.Nodes
				.FirstOrDefault(n => n.Key == "Targetable")?.Value.Nodes
				.FirstOrDefault(n => n.Key == "TargetTypes")?.Value.Value;

			Assert.That(targetTypes, Is.Not.Null,
				"SUPPLYCACHE has no Targetable.TargetTypes — this test is scanning nothing");

			return targetTypes.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
		}

		/// <summary>
		/// The shipped base auto-target priority: `AutoTargetPriority@FireAtWill: Priority: 1` on
		/// ^AutoTarget (defaults.yaml:330-331) overrides nothing else, so every other field is the
		/// engine default — ValidTargets {Ground, Water, Air}, InvalidTargets {NoAutoTarget}.
		/// </summary>
		static List<AutoTargetPriorityInfo> FireAtWillPriorities()
		{
			var info = new AutoTargetPriorityInfo();
			FieldLoader.LoadField(info, "Priority", "1");
			return new List<AutoTargetPriorityInfo> { info };
		}

		static int Band(params string[] targetTypes)
		{
			return AutoTarget.ResolveTargetPriorityBand(FireAtWillPriorities(), PlayerRelationship.Enemy,
				new BitSet<TargetableType>(targetTypes));
		}

		[Test]
		public void DroppedCrateIsNeverAutoTargeted()
		{
			var types = CacheTargetTypes();

			Assert.That(types, Does.Contain(Exclusion),
				$"SUPPLYCACHE.Targetable.TargetTypes must carry `{Exclusion}` — without it the crate's " +
				"`Ground` type matches the base FireAtWill priority and every idle unit in range opens " +
				$"fire on it unaided. Shipped value: {string.Join(", ", types)}");

			Assert.That(Band(types), Is.EqualTo(AutoTarget.NoTargetPriorityBand),
				"an enemy unit still resolves an auto-target priority band for the dropped crate, so " +
				"AutoTarget will acquire and shoot it");
		}

		[Test]
		public void TheExclusionIsWhatDoesTheWork()
		{
			// Guard the guard. If the crate were auto-targetable for some reason OTHER than its target
			// types — or if ResolveTargetPriorityBand answered "no band" for everything, the exact defect
			// AutoTargetPriorityBandTest was written for — the assertion above would pass while measuring
			// nothing. Strip the exclusion and the band MUST come back.
			var withoutExclusion = CacheTargetTypes().Where(t => t != Exclusion).ToArray();

			Assert.That(Band(withoutExclusion), Is.EqualTo(1),
				"stripping " + Exclusion + " did NOT restore an auto-target band, so the exclusion is not " +
				"what is suppressing acquisition and this test proves nothing about it");
		}

		[Test]
		public void ManualAttackStillBinds()
		{
			// The trap: an exclusion implemented as untargetability breaks the player's own attack order.
			// A crate must stay a legal target for an ordinary ground weapon so force-fire keeps working.
			var types = new BitSet<TargetableType>(CacheTargetTypes());

			var weapon = new WeaponInfo(new MiniYaml("", new List<MiniYamlNode>
			{
				new MiniYamlNode("ValidTargets", "Ground"),
			}));

			Assert.That(weapon.IsValidTarget(types), Is.True,
				"an ordinary Ground weapon can no longer target the dropped crate at all, so the player's " +
				"manual attack order has been broken along with auto-acquisition");

			Assert.That(CacheTargetTypes(), Does.Contain("Ground"),
				"SUPPLYCACHE lost its `Ground` target type — force-fire binds through weapon ValidTargets, " +
				"so removing it is exactly the untargetability mistake this test guards against");
		}
	}
}
