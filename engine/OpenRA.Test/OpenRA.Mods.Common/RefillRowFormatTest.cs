#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>The production tooltip's Refill row.</para>
	///
	/// <para>Both reported faults were notation, not data. A T-90 showed "AMMO 40 rounds" above
	/// "REFILL 8 × 30 = 240 supply", where 8 is a BATCH count — 40 rounds over a ReloadCount of 5 —
	/// but nothing said so, so it read as a second, contradictory round count. An RPG showed
	/// "1 × 30 = 30 supply", multiplying by one.</para>
	/// </summary>
	[TestFixture]
	public class RefillRowFormatTest
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

		/// <summary>A top-level node's locally-declared AmmoPool@1 values.</summary>
		static (int Ammo, int ReloadCount, int SupplyValue) Pool(string node, string file)
		{
			var actor = MiniYaml.FromFile(FindRules("ingame", file)).FirstOrDefault(n => n.Key == node);
			Assert.That(actor, Is.Not.Null, $"{node} is gone from {file}.");

			var pool = actor.Value.Nodes.FirstOrDefault(n => n.Key == "AmmoPool@1");
			Assert.That(pool, Is.Not.Null, $"{node} no longer declares AmmoPool@1 locally in {file}.");

			int Field(string key, int fallback)
			{
				var n = pool.Value.Nodes.FirstOrDefault(x => x.Key == key);
				return n != null && int.TryParse(n.Value.Value.Trim(), out var v) ? v : fallback;
			}

			return (Field("Ammo", 0), Field("ReloadCount", 1), Field("SupplyValue", 0));
		}

		[Test]
		public void AMultiBatchPoolNamesTheUnitOfEveryNumber()
		{
			// The T-90 shape. "5 rounds" is the only number besides the price, and it says what it is.
			Assert.That(AmmoPoolInfo.FormatRefill(70, 5, 8), Is.EqualTo("70 supply per 5 rounds"));
		}

		[Test]
		public void NoBareCountCanBeMistakenForARoundCount()
		{
			// The actual report: the leading batch count sat under a row reading "40 rounds".
			var row = AmmoPoolInfo.FormatRefill(70, 5, 8);

			Assert.That(row, Does.Not.StartWith("8"),
				"A bare leading count is the thing that read as rounds. If a batch count is shown " +
				"again it has to carry its unit.");
			Assert.That(row, Does.Not.Contain("×"),
				"The arithmetic is what invited the reader to check it against the row above.");
		}

		[Test]
		public void ASinglePurchasePoolJustStatesThePrice()
		{
			// Item 1 verbatim: the RPG (Ammo 1, ReloadCount 1, SupplyValue 30) said "1 × 30 = 30".
			Assert.That(AmmoPoolInfo.FormatRefill(30, 1, 1), Is.EqualTo("30 supply"));
		}

		[Test]
		public void ASinglePurchasePoolIsFlatWhateverItsBatchSize()
		{
			// A pool of 5 rounds bought in one batch of 5: still one transaction, still no rate.
			Assert.That(AmmoPoolInfo.FormatRefill(200, 5, 1), Is.EqualTo("200 supply"));
		}

		[Test]
		public void PerRoundPoolsReadAsPerRound()
		{
			// A Team Leader's grenade launcher: 6 rounds, ReloadCount 1, 8 supply each. "per 1 rounds"
			// would be the obvious way to get this wrong.
			Assert.That(AmmoPoolInfo.FormatRefill(8, 1, 6), Is.EqualTo("8 supply per round"));
		}

		[Test]
		public void TheRateIsWhatTheEngineActuallyCharges()
		{
			// The format claims one SupplyValue buys one BatchSize of rounds. That is TryServeBatch's
			// contract (AmmoPool.cs) and the reason a rate is honest here rather than a derived
			// per-round average: a pool one round short still pays a whole batch.
			var t90 = Pool("t90", "vehicles-russia.yaml");
			var batch = Math.Max(1, t90.ReloadCount);
			var batches = (t90.Ammo + batch - 1) / batch;

			Assert.That(AmmoPoolInfo.FormatRefill(t90.SupplyValue, batch, batches),
				Is.EqualTo($"{t90.SupplyValue} supply per {batch} rounds"));
		}

		[Test]
		public void TheTwoMainBattleTanksAreNoLongerPricedIdentically()
		{
			// They were byte-identical at SupplyValue 30, so the roster's two headline units were
			// economically indistinguishable. The split is justified in the YAML comments on both
			// pools (DU vs tungsten penetrator); this pins that it exists and points the right way.
			var abrams = Pool("abrams", "vehicles-america.yaml");
			var t90 = Pool("t90", "vehicles-russia.yaml");

			Assert.That(abrams.SupplyValue, Is.GreaterThan(t90.SupplyValue),
				"The Abrams' depleted-uranium M829 round is the dearer of the two. If this flips, " +
				"the justification recorded on both AmmoPool@1 comments no longer describes the data.");

			Assert.That(abrams.Ammo, Is.EqualTo(t90.Ammo),
				"Round COUNT was deliberately left equal — the retune priced ammunition and did not " +
				"touch how long either tank can fight.");
		}

		[Test]
		public void TankAmmoIsNoLongerTheCheapestThingOnTheRoster()
		{
			// Both tanks sat near 10% of platform cost while an IFV that costs 60% as much paid 43%.
			// The user asked for "a bit higher, like ... total 600, or something like that".
			foreach (var (node, file, cost) in new[]
			{
				("abrams", "vehicles-america.yaml", 2500),
				("t90", "vehicles-russia.yaml", 2400),
			})
			{
				var p = Pool(node, file);
				var batch = Math.Max(1, p.ReloadCount);
				var total = ((p.Ammo + batch - 1) / batch) * p.SupplyValue;

				Assert.That(total, Is.InRange(500, 700),
					$"{node}'s full main-gun refill is {total}, outside the band the retune aimed at.");
				Assert.That(total * 100 / cost, Is.InRange(20, 30),
					$"{node}'s refill is {total * 100 / cost}% of its {cost} cost. The point of the " +
					"retune was to stop main-gun ammunition being the roster's cheapest by that ratio.");
			}
		}
	}
}
