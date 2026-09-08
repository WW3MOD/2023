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
using OpenRA.Mods.Common.Warheads;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// VaporizeWarhead removes actors by IGNORING target types, which is the only way to reach the
	/// things it is for — WW3MOD protects several actors with a target type no weapon lists rather
	/// than with toughness, and a fireball is not a targeting decision. Dropping that test costs two
	/// safety properties that nothing else enforces, and this fixture is both of them.
	///
	/// 1. THE HEALTH GATE, which closes a failure that is SILENT rather than loud. Vaporizable.Tick
	///    finishes with Actor.Kill, and Actor.Kill returns immediately when the actor has no health
	///    trait (Actor.cs:634-640). Nothing else clears the trait's `active` flag, so a healthless
	///    actor that started the effect fades to alpha 0 and then stays alive, functional and
	///    completely invisible for the rest of the match. Nothing logs, nothing fails, and on a dense
	///    map the class is enormous — crop fields alone are 3187 of river-zeta's 4544 actors, and it
	///    includes `waypoint` and `spawnarea`, which scenario Lua keeps finding by name. A scenario
	///    whose waypoints went invisible would still pass; only its screenshot would be wrong.
	///
	///    The gate on the warhead makes that unreached. The gate in Begin makes it UNREACHABLE, which
	///    is the property worth pinning: it holds for a future caller that never goes through
	///    VaporizeWarhead at all.
	///
	/// 2. THE TWO EXEMPTIONS, which are load-bearing in ways their one-line removal does not show.
	///    Vaporizing a SUPPLYROUTE is immediate defeat, not a production lockout; vaporizing an
	///    in-flight missile body lets a MIRV or Dead Hand salvo shoot down its own later warheads.
	///
	/// No fixture here launches anything. The behavioural halves are pinned structurally, by asking
	/// what a method CALLS (IlScan) and what the shipped YAML SAYS.
	/// </summary>
	[TestFixture]
	public class VaporizeScopeTest
	{
		static MethodInfo CanVaporize()
		{
			var m = typeof(Vaporizable).GetMethod(nameof(Vaporizable.CanVaporize), BindingFlags.Public | BindingFlags.Static);
			Assert.That(m, Is.Not.Null,
				"Vaporizable.CanVaporize is gone. It is the single predicate the warhead and the trait share; " +
				"if it has been inlined into both, they can now silently disagree.");
			return m;
		}

		static IlScan.Result ScanFor(MethodBase method, string what)
		{
			var scan = IlScan.Scan(method);
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(1),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in {what} — the scanner is broken, " +
				"not the code clean.");
			return scan;
		}

		static bool Calls(IlScan.Result scan, MethodBase target)
		{
			return scan.Callees.Any(c => c.MetadataToken == target.MetadataToken && c.Module == target.Module);
		}

		[Test]
		public void CanVaporizeIsExactlyTheCanBeKilledPredicate()
		{
			var mortal = new ActorInfo("mortal", new HealthInfo());
			var healthless = new ActorInfo("healthless", new VaporizableInfo());

			Assert.That(Vaporizable.CanVaporize(mortal), Is.True,
				"an actor with a Health trait must be vaporisable — this predicate is what wires the whole feature.");

			Assert.That(Vaporizable.CanVaporize(healthless), Is.False,
				"an actor with NO health trait was reported vaporisable. Actor.Kill no-ops on such an actor " +
				"(Actor.cs:634-640) and nothing clears Vaporizable.active, so it would fade to fully " +
				"transparent and stay alive and functional forever. That is the silent failure this gate exists " +
				"to make unreachable.");
		}

		[Test]
		public void BeginRefusesAnActorItCouldNotKill()
		{
			var begin = typeof(Vaporizable).GetMethod(nameof(Vaporizable.Begin));
			Assert.That(begin, Is.Not.Null, "Vaporizable.Begin is gone.");

			var scan = ScanFor(begin, "Vaporizable.Begin");

			Assert.That(Calls(scan, CanVaporize()), Is.True,
				"Vaporizable.Begin no longer consults CanVaporize, so the invisible-zombie state is reachable " +
				"again from any caller that does not gate first. The guard belongs HERE, at the start of Begin — " +
				"NOT at the end of Tick. Clearing `active` after a Kill that did nothing looks equivalent and " +
				"silently restores husks: SpawnActorOnDeath re-reads ISuppressDeathRemains at RemovedFromWorld " +
				"and says at the site that the second check is the one that matters " +
				"(SpawnActorOnDeath.cs:134-138), and RemovedFromWorld runs at frame end, after Tick.");
		}

		[Test]
		public void TheWarheadAsksTheTraitsOwnQuestion()
		{
			var isValid = typeof(VaporizeWarhead).GetMethod(
				nameof(VaporizeWarhead.IsValidAgainst), new[] { typeof(Actor), typeof(Actor) });
			Assert.That(isValid, Is.Not.Null, "VaporizeWarhead no longer overrides IsValidAgainst(Actor, Actor).");
			Assert.That(isValid.DeclaringType, Is.EqualTo(typeof(VaporizeWarhead)),
				"VaporizeWarhead stopped overriding IsValidAgainst, so it has fallen back to the base " +
				"implementation and its ValidTargets list gates it again. The whole point of the warhead is that " +
				"a fireball is not a targeting decision: SUPPLYROUTE and ^TechBuilding are protected by a target " +
				"type nothing lists (NoAutoTarget), and the base test would spare them.");

			var scan = ScanFor(isValid, "VaporizeWarhead.IsValidAgainst");

			Assert.That(Calls(scan, CanVaporize()), Is.True,
				"VaporizeWarhead.IsValidAgainst no longer shares the trait's health predicate. Inlining the same " +
				"test in both places is how the two drift apart; a warhead that admits an actor the trait then " +
				"refuses is a wasted circle query, and the reverse is the invisible zombie.");

			var targetTest = typeof(Warhead).GetMethod("IsValidTarget", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.That(targetTest, Is.Not.Null, "Warhead.IsValidTarget has moved — this assertion is pinning nothing.");
			Assert.That(Calls(scan, targetTest), Is.False,
				"VaporizeWarhead.IsValidAgainst has started consulting target types again, which re-protects " +
				"every actor whose only defence is a target type no weapon lists. If a weapon genuinely needs to " +
				"spare something, take Vaporizable off the ACTOR with `-Vaporizable:` — that is the documented " +
				"opt-out and it is the mechanism the two shipped exemptions use.");

			var baseIsValid = typeof(Warhead).GetMethod(
				nameof(Warhead.IsValidAgainst), new[] { typeof(Actor), typeof(Actor) });
			Assert.That(Calls(scan, baseIsValid), Is.False,
				"VaporizeWarhead.IsValidAgainst calls base.IsValidAgainst, which runs the target-type test it " +
				"is supposed to replace. The override must reimplement the AffectsParent and relationship " +
				"clauses rather than delegate.");
		}

		[Test]
		public void TickStillEndsInTheKillThatCanQuietlyDoNothing()
		{
			var map = typeof(Vaporizable).GetInterfaceMap(typeof(ITick));
			var tick = map.TargetMethods.Single();
			var scan = ScanFor(tick, "Vaporizable.Tick");

			var kill = typeof(Actor).GetMethod(nameof(Actor.Kill));
			Assert.That(Calls(scan, kill), Is.True,
				"Vaporizable.Tick no longer ends in Actor.Kill. If the removal has moved to Dispose or to a " +
				"path that cannot silently no-op, the health gate above may no longer be needed — re-read the " +
				"reasoning on Vaporizable.CanVaporize before deleting it, rather than after.");
		}

		// ---- the two exemptions, read out of the shipped YAML ----

		const string Opt = "-Vaporizable";

		static DirectoryInfo RulesDir()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "mods", "ww3mod", "rules"));
				if (candidate.Exists)
					return candidate;
			}

			throw new DirectoryNotFoundException("could not locate mods/ww3mod/rules");
		}

		static List<MiniYamlNode> TopLevel(string relativeDir)
		{
			var dir = relativeDir == null
				? RulesDir()
				: new DirectoryInfo(Path.Combine(RulesDir().FullName, relativeDir));

			var nodes = new List<MiniYamlNode>();
			foreach (var file in dir.GetFiles("*.yaml", SearchOption.AllDirectories))
				nodes.AddRange(MiniYaml.FromFile(file.FullName));

			Assert.That(nodes.Count, Is.GreaterThan(50),
				$"only {nodes.Count} nodes parsed under {dir.FullName} — this fixture is scanning nothing, not passing.");

			return nodes;
		}

		static MiniYamlNode Find(IEnumerable<MiniYamlNode> nodes, string key)
		{
			var node = nodes.FirstOrDefault(n => n.Key == key);
			Assert.That(node, Is.Not.Null, $"`{key}` is not in the shipped rules — this fixture is pinning nothing.");
			return node;
		}

		[Test]
		public void TheSupplyRouteOptsOutOfVaporisation()
		{
			var sr = Find(TopLevel(null), "SUPPLYROUTE");

			Assert.That(sr.Value.Nodes.Any(n => n.Key == Opt), Is.True,
				"SUPPLYROUTE lost its `-Vaporizable:` line, so one nuke is now an instant win. The SR is not " +
				"invulnerable — it has Health: HP: 75000 and is merely UNTARGETABLE, via a target type no weapon " +
				"lists — and VaporizeWarhead does not consult target types, so nothing else stands between a " +
				"fireball and the beachhead. It carries MustBeDestroyed: RequiredForShortGame, so a player who " +
				"loses it has no required units (PlayerExtensions.cs:22-23) and their objective is failed on the " +
				"spot (ConquestVictoryConditions.cs:76-77). That replaces the designed, relievable, reversible " +
				"contestation siege with an instant purchase.");

			Assert.That(sr.Value.Nodes.Any(n => n.Key == "Health"), Is.True,
				"SUPPLYROUTE no longer declares Health. If it has become genuinely invulnerable the exemption " +
				"above may be redundant — but check what replaced it before removing anything.");
		}

		[Test]
		public void InFlightMissileBodiesOptOutOfVaporisation()
		{
			var template = Find(TopLevel(null), "^ShootableMissile");

			Assert.That(template.Value.Nodes.Any(n => n.Key == Opt), Is.True,
				"^ShootableMissile lost its `-Vaporizable:` line, so every in-flight missile body in the game is " +
				"vaporisable again. Warhead.AffectsParent only spares victim == firedBy, so a SIBLING warhead in " +
				"the same salvo is fair game; VaporizeWarhead's radius test is horizontal-only and ignores " +
				"altitude; and the largest shipped fireball is a ~16-cell disc. A MIRV or Dead Hand wave would " +
				"vaporize its own later warheads mid-descent, which reads as a nuke that randomly did nothing " +
				"and breaks DoomsdayStrike's scheduled-arrival arithmetic.");
		}

		[Test]
		public void EveryVaporizeWarheadIsSizedToAFireball()
		{
			var found = 0;
			foreach (var weapon in TopLevel("weapons"))
			{
				foreach (var wh in weapon.Value.Nodes)
				{
					if (wh.Value.Value?.Trim() != "Vaporize")
						continue;

					found++;

					var radius = wh.Value.Nodes.FirstOrDefault(n => n.Key == "Radius");
					Assert.That(radius, Is.Not.Null,
						$"{weapon.Key}'s {wh.Key} sets no Radius. FieldLoader.Require would catch this at map " +
						"load, but only for someone who loaded the map.");

					Assert.That(radius.Value.Value?.Trim(), Is.Not.EqualTo("0").And.Not.EqualTo("0c0"),
						$"{weapon.Key}'s {wh.Key} has a zero Radius, which makes DoImpact return immediately " +
						"without touching anything — silently, and with no lint error.");
				}
			}

			Assert.That(found, Is.GreaterThanOrEqualTo(14),
				$"only {found} Vaporize warheads found in mods/ww3mod/rules/weapons. The nuclear arsenal wires " +
				"one per weapon; if that has been unwired, the user-visible feature is gone and the exemptions " +
				"above are guarding nothing.");
		}
	}
}
