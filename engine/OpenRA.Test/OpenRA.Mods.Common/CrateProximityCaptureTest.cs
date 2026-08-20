#region Copyright & License Information
/*
 * WW3MOD dropped-crate proximity capture — corpus pin.
 *
 * "Getting close to one should capture it for ourselves instantly, so we can use the resources
 * ourselves." (live play, 2026-08-20). The mechanism itself shipped in 30f966b3 — SUPPLYCACHE carries
 * ProximityCapturable — but at 1c512 it required a unit to stop essentially ON the crate, and it was
 * unreachable in practice anyway because units auto-targeted and destroyed crates first (fixed in the
 * preceding commit). What this file pins is that the radius stays inside the two bounds that give it
 * meaning, and that the units the player actually walks up to a crate can in fact take it.
 *
 * WHY 2c512 RATHER THAN A NUMBER PICKED TO FEEL RIGHT. It is the radius the Logistics Center already
 * absorbs a cache at (AbsorbsSupplyCache, structures.yaml:418, and the engine default at
 * AbsorbsSupplyCache.cs:22). Both answer the same question — how close must you be for a crate to come
 * to you — one for a building absorbing, one for a unit capturing, so a silent disagreement between
 * them is worse than either value.
 *
 * The CEILING is the crate's own SupplyProvider.Range. Capture must never reach further than the aura
 * in which the crate would already be serving you: a crate you cannot be supplied by is not a crate you
 * should be able to take from across the street.
 *
 * NOTE ON AIRCRAFT, which looks like an omission and is not. ProximityCapturable registers its trigger
 * with vRange = WDist.Zero (ProximityCapturable.cs:85), and ProximityTrigger.Tick treats a zero vRange
 * as "no altitude filter at all" (ActorMap.cs:144: `vRange.Length == 0 || ...`). So an aircraft at
 * cruise altitude is inside the trigger horizontally and would capture on overflight if `Plane` were a
 * captor type. It deliberately is not — see AircraftCannotCaptureByOverflight.
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
	public class CrateProximityCaptureTest
	{
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

		static MiniYamlNode SupplyCache()
		{
			var cache = MiniYaml.FromFile(FindRules("misc.yaml")).FirstOrDefault(n => n.Key == "SUPPLYCACHE");
			Assert.That(cache, Is.Not.Null, "SUPPLYCACHE not found in misc.yaml — this test is scanning nothing");
			return cache;
		}

		static string Field(MiniYamlNode actor, string trait, string field)
		{
			return actor.Value.Nodes
				.FirstOrDefault(n => n.Key == trait)?.Value.Nodes
				.FirstOrDefault(n => n.Key == field)?.Value.Value;
		}

		static WDist Distance(string raw, string what)
		{
			Assert.That(raw, Is.Not.Null, what + " is not set — this test is scanning nothing");
			Assert.That(WDist.TryParse(raw, out var result), Is.True, $"{what} is not a parseable WDist: {raw}");
			return result;
		}

		static string[] CaptorTypes()
		{
			var raw = Field(SupplyCache(), "ProximityCapturable", "CaptorTypes");
			Assert.That(raw, Is.Not.Null, "SUPPLYCACHE.ProximityCapturable has no CaptorTypes");
			return raw.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
		}

		[Test]
		public void WalkingUpToACrateTakesIt()
		{
			var cache = SupplyCache();

			Assert.That(cache.Value.Nodes.Any(n => n.Key == "ProximityCapturable"), Is.True,
				"SUPPLYCACHE has no ProximityCapturable at all, so getting close to a crate does nothing");

			var range = Distance(Field(cache, "ProximityCapturable", "Range"), "ProximityCapturable.Range");

			// The floor. Below one whole cell plus its own footprint the unit has to stop ON the crate
			// rather than walk up to it, which is the "I got close and nothing happened" report.
			Assert.That(range.Length, Is.GreaterThanOrEqualTo(new WDist(2560).Length),
				"the capture radius is tighter than the Logistics Center's own AbsorbsSupplyCache range " +
				"(2c512), so a unit can stand beside a crate without taking it while a building would " +
				"have absorbed it from further away");

			// Sticky, or the crate reverts to its original owner the moment the captor walks on — the
			// player would watch it change colour and change back.
			Assert.That(Field(cache, "ProximityCapturable", "Sticky"), Is.EqualTo("true").IgnoreCase,
				"capture is not Sticky, so a captured crate reverts as soon as the capturing unit leaves");
		}

		[Test]
		public void CaptureNeverReachesFurtherThanTheCrateSuppliesYou()
		{
			var cache = SupplyCache();
			var capture = Distance(Field(cache, "ProximityCapturable", "Range"), "ProximityCapturable.Range");
			var supply = Distance(Field(cache, "SupplyProvider", "Range"), "SupplyProvider.Range");

			Assert.That(capture.Length, Is.LessThanOrEqualTo(supply.Length),
				$"capture radius ({capture}) now exceeds the crate's own supply aura ({supply}) — a unit " +
				"can take a crate from outside the range at which that crate would have served it");
		}

		[Test]
		public void EveryGroundUnitCanCaptureACrate()
		{
			// ProximityCapturable.CanBeCapturedBy is `pc.Types.Overlaps(Info.CaptorTypes)`
			// (ProximityCapturable.cs:138). Reproduce that against the types the shipped GROUND templates
			// actually declare, so renaming a captor type on either side fails here rather than silently
			// making crates uncapturable.
			var captorTypes = CaptorTypes();

			var groundTemplates = new Dictionary<string, string>
			{
				{ "^Soldier", Path.GetFileName(FindRules("ingame", "infantry.yaml")) },
				{ "^Vehicle", Path.GetFileName(FindRules("ingame", "vehicles.yaml")) },
			};

			var checkedTemplates = 0;
			var cannotCapture = new List<string>();

			foreach (var (template, _) in groundTemplates)
			{
				var file = template == "^Soldier"
					? FindRules("ingame", "infantry.yaml")
					: FindRules("ingame", "vehicles.yaml");

				var node = MiniYaml.FromFile(file).FirstOrDefault(n => n.Key == template);
				if (node == null)
					continue;

				var types = Field(node, "ProximityCaptor", "Types");
				if (types == null)
				{
					cannotCapture.Add($"{template} (no ProximityCaptor at all)");
					continue;
				}

				checkedTemplates++;

				var declared = types.Split(',').Select(t => t.Trim());
				if (!declared.Any(t => captorTypes.Contains(t)))
					cannotCapture.Add($"{template} (Types: {types})");
			}

			// Guard the guard: a rename of either template would otherwise report a clean result.
			Assert.That(checkedTemplates, Is.EqualTo(groundTemplates.Count),
				"did not resolve every ground template's ProximityCaptor — the scan itself is broken");

			Assert.That(cannotCapture, Is.Empty,
				"these ground templates declare no captor type that SUPPLYCACHE accepts, so units built " +
				$"from them walk over a crate without taking it (CaptorTypes: {string.Join(", ", captorTypes)}): " +
				string.Join(", ", cannotCapture));
		}

		[Test]
		public void AircraftCannotCaptureByOverflight()
		{
			// Pinning a DECISION, not an accident. The proximity trigger ignores altitude entirely
			// (ActorMap.cs:144), so adding `Plane` here would let any aircraft — a transport, a bomber at
			// cruise height — sweep up crates it happens to fly across. Ground contact is the mechanic the
			// player described ("getting close to one"), so aircraft are excluded until the trigger can
			// carry a real vertical bound.
			Assert.That(CaptorTypes(), Does.Not.Contain("Plane"),
				"`Plane` was added to SUPPLYCACHE.CaptorTypes — because ProximityCapturable registers its " +
				"trigger with vRange 0 and ProximityTrigger treats that as 'no altitude filter', this lets " +
				"aircraft capture crates by flying over them at any height");
		}
	}
}
