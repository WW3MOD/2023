#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// SupplyRouteContestation carries the "does this actor add to enemyValue" filter twice: inlined in
	/// RecalculateForces (sim code, deliberately not refactored while a benchmark baseline is taken on
	/// it) and copied into CountsTowardEnemyValue, which HasEnemyInRange — the verdict v9
	/// ticks_with_enemy_in_ring telemetry — uses. This fixture pins both to one truth table: the
	/// RecalculateForces side by its source text (an actor-bound method has no seam to call without a
	/// World), transcribed below as an oracle; the copy by calling it. Either side drifting fails here.
	/// </summary>
	[TestFixture]
	public class SupplyRouteEnemyFilterTest
	{
		// The two filter lines RecalculateForces must still contain, whitespace-normalized. If this
		// test fails because RecalculateForces changed, update CountsTowardEnemyValue, the oracle below
		// and these lines together.
		const string SkipLine = "if (valued == null || valued.Cost <= 0) continue;";
		const string EnemyLine = "if (rel == PlayerRelationship.Enemy) enemyValue += valued.Cost;";

		// RecalculateForces' filter, transcribed from SkipLine + EnemyLine: skip when there is no
		// Valued or its cost is not positive; otherwise count toward enemyValue only for Enemy.
		static bool RecalculateForcesOracle(PlayerRelationship rel, int? cost)
		{
			if (cost == null || cost <= 0)
				return false;

			return rel == PlayerRelationship.Enemy;
		}

		static readonly int?[] Costs = { null, int.MinValue, -5, 0, 1, 500, int.MaxValue };

		static string FindEngine(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "engine", Path.Combine(relative));
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate engine/" + string.Join("/", relative));
		}

		static string RecalculateForcesBody()
		{
			var src = File.ReadAllText(FindEngine("OpenRA.Mods.Common", "Traits", "SupplyRouteContestation.cs"));
			var start = src.IndexOf("void RecalculateForces()", StringComparison.Ordinal);
			Assert.That(start, Is.GreaterThanOrEqualTo(0), "RecalculateForces not found in SupplyRouteContestation.cs");
			var end = src.IndexOf("cachedNetEnemySurplus =", start, StringComparison.Ordinal);
			Assert.That(end, Is.GreaterThan(start), "RecalculateForces no longer assigns cachedNetEnemySurplus");
			return Regex.Replace(src.Substring(start, end - start), @"\s+", " ");
		}

		[Test]
		public void RecalculateForcesStillUsesThePinnedFilter()
		{
			var body = RecalculateForcesBody();
			Assert.That(body, Does.Contain(SkipLine), "RecalculateForces' Valued/cost skip changed — update CountsTowardEnemyValue to match");
			Assert.That(body, Does.Contain(EnemyLine), "RecalculateForces' enemy test changed — update CountsTowardEnemyValue to match");
		}

		[Test]
		public void CopyMatchesRecalculateForcesOnEveryRelationshipAndCost()
		{
			foreach (PlayerRelationship rel in Enum.GetValues(typeof(PlayerRelationship)))
				foreach (var cost in Costs)
					Assert.That(SupplyRouteContestation.CountsTowardEnemyValue(rel, cost), Is.EqualTo(RecalculateForcesOracle(rel, cost)),
						$"rel={rel} cost={(cost?.ToString() ?? "null")}");
		}

		[Test]
		public void OnlyAPricedEnemyCounts()
		{
			// The fixture's anchor rows, so a wrong oracle cannot make the comparison above vacuous.
			Assert.That(SupplyRouteContestation.CountsTowardEnemyValue(PlayerRelationship.Enemy, 500), Is.True);
			Assert.That(SupplyRouteContestation.CountsTowardEnemyValue(PlayerRelationship.Enemy, 0), Is.False);
			Assert.That(SupplyRouteContestation.CountsTowardEnemyValue(PlayerRelationship.Enemy, null), Is.False);
			Assert.That(SupplyRouteContestation.CountsTowardEnemyValue(PlayerRelationship.Ally, 500), Is.False);
			Assert.That(SupplyRouteContestation.CountsTowardEnemyValue(PlayerRelationship.Neutral, 500), Is.False);
		}
	}
}
