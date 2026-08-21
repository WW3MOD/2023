#region Copyright & License Information
/*
 * WW3MOD dropped-crate resupply parity — corpus pin.
 *
 * "The dropped supply crate doesn't seem to rearm units, even when it has plenty of supplies left. It
 * should rearm just the same as the supply truck does, same radius/speed etc." (live play, 2026-08-21).
 *
 * TWO SEPARATE DEFECTS sat behind that one sentence, and only one of them is about speed.
 *
 * 1. INERT WITH SUPPLY LEFT. SupplyProviderInfo.RestockThreshold defaults to 50 and SUPPLYCACHE never
 *    overrode it, so the tick ladder stopped serving at 1..49 supply — while RemoveBelowSupply: 1 kept
 *    the crate in the world until supply reached 0, which serving was the only thing that could have
 *    achieved. A crate in that band parked forever with a visible supply bar, serving nobody. It is
 *    reachable two ways: drained down into the band, or DROPPED into it, since DropsSupplyCache seeds
 *    the crate with the truck's exact remaining load (DropsSupplyCache.cs:199) and an Evacuate-stance
 *    truck serves below its own threshold. The threshold exists to reserve fuel-for-a-trip a crate does
 *    not have.
 *
 * 2. A QUARTER THE RATE, A CELL SHORTER. Range 4c0 vs the truck's 5c0, RearmDelay 25 vs 6.
 *
 * The first is what makes the report say "doesn't", the second is what makes it say "same speed".
 *
 * Reads the shipped YAML rather than a fixture, and resolves unset fields through the real
 * SupplyProviderInfo defaults, because an omitted field is exactly how defect 1 arose — a fixture that
 * restated the crate's config would have been green throughout.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupplyCacheTruckParityTest
	{
		static readonly SupplyProviderInfo Defaults = new SupplyProviderInfo();

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

		static MiniYamlNode Actor(string name, params string[] relative)
		{
			var node = MiniYaml.FromFile(FindRules(relative)).FirstOrDefault(n => n.Key == name);
			Assert.That(node, Is.Not.Null, $"{name} not found in {string.Join("/", relative)} — this test is scanning nothing");
			return node;
		}

		static MiniYamlNode SupplyCache() { return Actor("SUPPLYCACHE", "misc.yaml"); }

		static MiniYamlNode Truck() { return Actor("TRUK", "ingame", "vehicles.yaml"); }

		static string Field(MiniYamlNode actor, string trait, string field)
		{
			return actor.Value.Nodes
				.FirstOrDefault(n => n.Key == trait)?.Value.Nodes
				.FirstOrDefault(n => n.Key == field)?.Value.Value;
		}

		static WDist Distance(MiniYamlNode actor, string trait, string field)
		{
			var raw = Field(actor, trait, field);
			Assert.That(raw, Is.Not.Null, $"{actor.Key}.{trait}.{field} is not set — this test is scanning nothing");
			Assert.That(WDist.TryParse(raw, out var result), Is.True, $"{actor.Key}.{trait}.{field} is not a parseable WDist: {raw}");
			return result;
		}

		/// <summary>
		/// An integer SupplyProvider field as it will actually be loaded: the YAML value if the actor
		/// states one, otherwise the engine default. Reading the default rather than assuming the field
		/// is present is the whole point — RestockThreshold was absent from SUPPLYCACHE, and "absent"
		/// silently meant 50.
		/// </summary>
		static int ProviderInt(MiniYamlNode actor, string field, int engineDefault)
		{
			var raw = Field(actor, "SupplyProvider", field);
			if (raw == null)
				return engineDefault;

			Assert.That(int.TryParse(raw, out var value), Is.True, $"{actor.Key}.SupplyProvider.{field} is not an integer: {raw}");
			return value;
		}

		static bool ProviderBool(MiniYamlNode actor, string field, bool engineDefault)
		{
			var raw = Field(actor, "SupplyProvider", field);
			return raw == null ? engineDefault : bool.Parse(raw.Trim());
		}

		[Test]
		public void ACrateWithSupplyLeftStillServesIt()
		{
			var cache = SupplyCache();

			var restockThreshold = ProviderInt(cache, "RestockThreshold", Defaults.RestockThreshold);
			var removeBelow = ProviderInt(cache, "RemoveBelowSupply", Defaults.RemoveBelowSupply);
			var evacuateOnResidue = ProviderBool(cache, "EvacuateOnUnusableResidue", Defaults.EvacuateOnUnusableResidue);
			var hasRestockHost = Field(cache, "SupplyProvider", "RestockActors") != null;

			// SupplyProvider.KeepServingBelowThreshold: only a residue-evacuating provider that will NOT
			// drive itself home keeps serving under the threshold. Derived, not assumed, so that turning
			// either field on later re-derives this rather than silently invalidating the test.
			var keepServingBelowThreshold = evacuateOnResidue && !hasRestockHost;

			// The band that can strand a crate: holding enough supply to stay in the world
			// (>= RemoveBelowSupply) but less than the level at which it will part with any of it.
			// If the two fields are matched, this band is empty and there is nothing to check.
			var strandedFloor = Math.Max(removeBelow, 1);
			for (var supply = strandedFloor; supply < Math.Max(restockThreshold, strandedFloor); supply++)
			{
				var reserving = SupplyProvider.ReservesRemainderForRestock(
					supply, restockThreshold, hasActiveTarget: false, keepServingBelowThreshold);

				Assert.That(reserving, Is.False,
					$"a dropped crate holding {supply} supply serves nobody: RestockThreshold is " +
					$"{restockThreshold} (engine default {Defaults.RestockThreshold} when the field is omitted), so it " +
					$"withholds its whole remaining load — yet RemoveBelowSupply is {removeBelow}, so it does not " +
					"despawn either. It parks in the world with a visible supply bar, permanently inert. A crate has " +
					"no drive home to reserve supply for; set RestockThreshold: 0 on SUPPLYCACHE.");
			}
		}

		[Test]
		public void CrateRadiusAndRateMatchTheTruck()
		{
			var cache = SupplyCache();
			var truck = Truck();

			var cacheRange = Distance(cache, "SupplyProvider", "Range");
			var truckRange = Distance(truck, "SupplyProvider", "Range");
			var cacheDelay = ProviderInt(cache, "RearmDelay", Defaults.RearmDelay);
			var truckDelay = ProviderInt(truck, "RearmDelay", Defaults.RearmDelay);

			// Multiple, so a radius regression does not hide a rate regression behind it. The user named
			// both in one breath ("same radius/speed etc.") and they failed together; a run that reported
			// only the first would send someone to fix half of it.
			Assert.Multiple(() =>
			{
				Assert.That(cacheRange.Length, Is.EqualTo(truckRange.Length),
					$"the crate's supply aura ({cacheRange}) does not match the truck's ({truckRange}). A crate is the " +
					"truck's own load set on the ground and serves the same infantry through the same push, so a player " +
					"who drops one has chosen a stationary resupply, not a weaker one.");

				Assert.That(cacheDelay, Is.EqualTo(truckDelay),
					$"the crate hands out one batch every {cacheDelay} ticks against the truck's {truckDelay} — " +
					$"{(double)cacheDelay / truckDelay:0.#}x slower for the same supply. \"It should rearm just the same " +
					"as the supply truck does, same radius/speed etc.\"");
			});
		}

		[Test]
		public void TheDrawnRangeCircleMatchesTheAuraItDescribes()
		{
			// GUARD, not a defect pin: these two agreed before the parity change and must go on agreeing
			// after it. RenderRangeCircle.FallbackRange is the only thing that tells a player how far a
			// crate reaches, so a stale copy of the old 4c0 would draw a circle the crate does not honour.
			var cache = SupplyCache();

			Assert.That(Distance(cache, "RenderRangeCircle@Supply", "FallbackRange").Length,
				Is.EqualTo(Distance(cache, "SupplyProvider", "Range").Length),
				"SUPPLYCACHE draws a supply circle at a radius its SupplyProvider does not actually serve to");
		}

		[Test]
		public void AProviderWithATripHomeStillReservesItsRemainder()
		{
			// The other side of the same predicate, so the fix above cannot be mistaken for "thresholds
			// are pointless". A truck below its threshold with no customer holds its remainder back to
			// afford the drive to a Logistics Center.
			Assert.That(SupplyProvider.ReservesRemainderForRestock(49, 50, hasActiveTarget: false, keepServingBelowThreshold: false), Is.True);

			// Mid-cycle: an active customer is served before the reservation applies.
			Assert.That(SupplyProvider.ReservesRemainderForRestock(49, 50, hasActiveTarget: true, keepServingBelowThreshold: false), Is.False);

			// An evacuating truck with nowhere to restock keeps serving down to the last usable batch.
			Assert.That(SupplyProvider.ReservesRemainderForRestock(49, 50, hasActiveTarget: false, keepServingBelowThreshold: true), Is.False);

			// At or above the threshold there is nothing to reserve.
			Assert.That(SupplyProvider.ReservesRemainderForRestock(50, 50, hasActiveTarget: false, keepServingBelowThreshold: false), Is.False);

			// Threshold 0 disables the reservation outright — the stationary-cache configuration.
			Assert.That(SupplyProvider.ReservesRemainderForRestock(1, 0, hasActiveTarget: false, keepServingBelowThreshold: false), Is.False);
		}
	}
}
