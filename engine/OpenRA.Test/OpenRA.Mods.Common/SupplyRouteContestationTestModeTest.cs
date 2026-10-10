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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// SupplyRouteContestationInfo.RunInTestMode: the opt-in that lets a scenario watch the win
	/// condition resolve end to end. ResolveTeamElimination used to return unconditionally under
	/// TestMode, so no autotest could ever see a contested Supply Route mark its owner Lost.
	///
	/// Pinned here: the gating decision itself, that the shipped default is OFF, that nothing shipped
	/// turns it on (so every shipped profile and existing scenario is byte-identical), and that
	/// ResolveTeamElimination still asks the predicate rather than an inlined copy of it. The
	/// behavioural half — a full defeat bar actually marking the owner Lost — is the
	/// test-contestation-defeat-resolves scenario.
	/// </summary>
	[TestFixture]
	public class SupplyRouteContestationTestModeTest
	{
		[Test]
		public void OutsideTestModeEliminationAlwaysResolves()
		{
			Assert.That(SupplyRouteContestation.ResolvesEliminationInSession(false, false), Is.True);
			Assert.That(SupplyRouteContestation.ResolvesEliminationInSession(false, true), Is.True,
				"RunInTestMode must never be able to switch the win condition OFF in a real match.");
		}

		[Test]
		public void InsideTestModeEliminationResolvesOnlyWhenOptedIn()
		{
			Assert.That(SupplyRouteContestation.ResolvesEliminationInSession(true, false), Is.False,
				"The default TestMode behaviour — harness owns the verdict — changed.");
			Assert.That(SupplyRouteContestation.ResolvesEliminationInSession(true, true), Is.True);
		}

		[Test]
		public void DefaultIsOff()
		{
			Assert.That(new SupplyRouteContestationInfo().RunInTestMode, Is.False,
				"RunInTestMode must default false: every existing autotest relies on contestation never marking a WinState.");
		}

		[Test]
		public void ResolveTeamEliminationConsultsThePredicate()
		{
			var resolve = typeof(SupplyRouteContestation).GetMethod("ResolveTeamElimination", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.That(resolve, Is.Not.Null, "SupplyRouteContestation.ResolveTeamElimination is gone.");

			var predicate = typeof(SupplyRouteContestation).GetMethod(
				nameof(SupplyRouteContestation.ResolvesEliminationInSession), BindingFlags.Public | BindingFlags.Static);

			var scan = IlScan.Scan(resolve);
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(1),
				$"IL scan resolved only {scan.ResolvedCalls} tokens — the scanner is broken, not the code clean.");
			Assert.That(scan.Callees.Any(c => c.MetadataToken == predicate.MetadataToken && c.Module == predicate.Module), Is.True,
				"ResolveTeamElimination no longer gates on ResolvesEliminationInSession. An inlined TestMode check can " +
				"silently drop the RunInTestMode opt-in, and the only witness would be a scenario timing out.");
		}

		[Test]
		public void NothingShippedOptsIn()
		{
			var modDir = ModDir();
			var offenders = new List<string>();
			foreach (var sub in new[] { "rules", "maps" })
			{
				var dir = new DirectoryInfo(Path.Combine(modDir.FullName, sub));
				foreach (var file in dir.GetFiles("*.yaml", SearchOption.AllDirectories))
					Walk(MiniYaml.FromFile(file.FullName), file.FullName, offenders);
			}

			Assert.That(offenders, Is.Empty,
				"Shipped content sets SupplyRouteContestation.RunInTestMode. It is a scenario-only switch: " +
				"turning it on in mods/ww3mod makes every autotest and tournament resolve WinStates mid-run.");
		}

		static void Walk(IEnumerable<MiniYamlNode> nodes, string file, List<string> offenders)
		{
			foreach (var n in nodes)
			{
				if (n.Key.Split('@')[0] == "SupplyRouteContestation" && n.Value.Nodes.Any(c => c.Key == "RunInTestMode"))
					offenders.Add(file);

				Walk(n.Value.Nodes, file, offenders);
			}
		}

		static DirectoryInfo ModDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "mods", "ww3mod"));
				if (candidate.Exists)
					return candidate;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod");
		}
	}
}
