#region Copyright & License Information
/*
 * WW3MOD infantry seek dropped crates — corpus pin.
 *
 * USER RULING, 2026-08-21. A dropped SUPPLYCACHE used to be push-only: it served whoever happened to
 * stand inside its aura, and nothing ever put anyone there. The reason is one list —
 * `Rearmable.RearmActors` gates AmmoPool.ChooseResupplier, which is the ONLY host-discovery path in
 * the engine (AutoSeekSupplies routes solely through it), and no RearmActors list in the corpus named
 * `supplycache`. economy.md documented that as a deliberate property of the design. The user
 * overruled it: infantry should walk to a crate the way they already walk to a Logistics Center.
 *
 * WHAT THIS FILE PROTECTS is the completeness of that list, because the failure mode is silent and
 * per-template. There is no single ^Soldier-level RearmActors to edit — the field is declared 14
 * times, once per infantry template that carries a Rearmable — so a template added later, or one
 * edited without knowing about this ruling, simply never seeks crates and nothing anywhere complains.
 * The rifleman would work in the playtest and the sniper would not.
 *
 * Reads the shipped YAML rather than a fixture: the thing being protected is the corpus.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyCacheSeekTest
	{
		const string Cache = "supplycache";

		static string FindRules(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var parts = new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(relative).ToArray();
				var candidate = Path.Combine(parts);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/" + string.Join("/", relative));
		}

		/// <summary>
		/// Every actor in infantry.yaml declaring a Rearmable, paired with the RearmActors it names.
		/// Enumerated from the file rather than listed here on purpose: a NEW infantry template must be
		/// caught by this test, and a hardcoded roster of the fourteen known today would not see it.
		/// </summary>
		static List<(string Actor, string[] Hosts)> InfantryRearmHosts()
		{
			var found = new List<(string, string[])>();

			foreach (var actor in MiniYaml.FromFile(FindRules("ingame", "infantry.yaml")))
			{
				var rearmable = actor.Value.Nodes.FirstOrDefault(n => n.Key == "Rearmable");
				if (rearmable == null)
					continue;

				var raw = rearmable.Value.Nodes.FirstOrDefault(n => n.Key == "RearmActors")?.Value.Value;
				var hosts = raw == null
					? Array.Empty<string>()
					: raw.Split(',').Select(h => h.Trim()).Where(h => h.Length > 0).ToArray();

				found.Add((actor.Key, hosts));
			}

			return found;
		}

		[Test]
		public void EveryRearmableInfantryTemplateCanWalkToACrate()
		{
			var templates = InfantryRearmHosts();

			// Guard the guard. If the scan resolved nothing — file moved, MiniYaml shape changed — an
			// empty roster would report a clean pass while protecting nothing at all.
			Assert.That(templates, Is.Not.Empty,
				"found no infantry template declaring a Rearmable — this test is scanning nothing");

			var cannotSeek = templates
				.Where(t => !t.Hosts.Contains(Cache))
				.Select(t => t.Hosts.Length == 0
					? $"{t.Actor} (Rearmable with no RearmActors at all)"
					: $"{t.Actor} (RearmActors: {string.Join(", ", t.Hosts)})")
				.ToArray();

			Assert.That(cannotSeek, Is.Empty,
				$"these infantry templates do not name `{Cache}` in RearmActors, so soldiers built from them " +
				"never walk to a dropped supply crate — AmmoPool.ChooseResupplier filters candidate hosts on " +
				"exactly this list and is the only host-discovery path in the engine, so the crate stays " +
				"push-only for them however close or however full it is (user ruling 2026-08-21): " +
				string.Join(", ", cannotSeek));
		}

		[Test]
		public void SeekingACrateDoesNotReplaceTheHostsInfantryAlreadyHad()
		{
			// The ruling ADDS a host. A find-and-replace that swapped `truk` or `logisticscenter` out
			// while adding the crate would satisfy the test above and quietly strand every soldier the
			// moment no crate is on the map.
			var missing = InfantryRearmHosts()
				.Where(t => !t.Hosts.Contains("truk") || !t.Hosts.Contains("logisticscenter"))
				.Select(t => $"{t.Actor} (RearmActors: {string.Join(", ", t.Hosts)})")
				.ToArray();

			Assert.That(missing, Is.Empty,
				"these infantry templates lost `truk` or `logisticscenter` from RearmActors — adding the " +
				"crate as a destination must not remove the two hosts that existed before it: " +
				string.Join(", ", missing));
		}

		[Test]
		public void TheCrateIsAValidHostForTheListToPointAt()
		{
			// RearmActors is matched against `a.Info.Name` and is otherwise unvalidated, so a typo, a
			// rename of SUPPLYCACHE, or a crate that lost its SupplyProvider would leave all fourteen
			// lists naming an actor that can never be returned — with no error anywhere.
			var cache = MiniYaml.FromFile(FindRules("misc.yaml"))
				.FirstOrDefault(n => string.Equals(n.Key, Cache, StringComparison.OrdinalIgnoreCase));

			Assert.That(cache, Is.Not.Null,
				$"infantry RearmActors names `{Cache}`, but no such actor exists in misc.yaml — " +
				"AmmoPool.ChooseResupplier matches on actor name and would silently never find one");

			Assert.That(cache.Value.Nodes.Any(n => n.Key == "SupplyProvider"), Is.True,
				$"`{Cache}` carries no SupplyProvider, so ChooseResupplier's ActorsHavingTrait<SupplyProvider> " +
				"query can never return it however many RearmActors lists name it");
		}
	}
}
