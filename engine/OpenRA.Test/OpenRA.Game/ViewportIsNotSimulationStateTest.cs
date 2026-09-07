#region Copyright & License Information
/*
 * WW3MOD viewport/simulation separation guard (2026-09-06).
 *
 * Written alongside the screen-shake rework. That model displaces Viewport.CenterLocation by an
 * amount derived from a per-client hash, which is only safe because the viewport is pure view
 * state — if any synchronised trait read it, a cosmetic camera wobble would become a desync, and
 * every choice in ScreenShakeModel would have to be re-litigated as a sync hazard rather than a
 * rendering one.
 *
 * ISync is OpenRA's own marker for "this participates in the sync hash" (Actor.cs tests for it
 * before hashing anything), so "no ISync implementor reads the viewport" is the load-bearing claim
 * stated in the engine's own vocabulary rather than in a hand-maintained list of type names.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ViewportIsNotSimulationStateTest
	{
		[Test]
		public void NoSynchronisedTypeReadsTheViewport()
		{
			var viewportMembers = typeof(Viewport)
				.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
				.ToDictionary(m => m.MetadataToken, m => m.Name);

			Assert.That(viewportMembers.Count, Is.GreaterThan(10),
				"Found almost no Viewport members — this test no longer scans what it claims to.");

			var assemblies = new[]
			{
				typeof(Viewport).Assembly,
				typeof(OpenRA.Mods.Common.Traits.ShakeOnDeath).Assembly
			};

			var syncTypes = assemblies
				.SelectMany(a => a.GetTypes())
				.Where(t => typeof(ISync).IsAssignableFrom(t) && !t.IsInterface)
				.ToArray();

			Assert.That(syncTypes.Length, Is.GreaterThan(50),
				$"Only {syncTypes.Length} ISync types found — the reflection above is wrong, not the code clean.");

			var resolvedCalls = 0;
			var offenders = new List<string>();
			var renderMethods = 0;

			foreach (var type in syncTypes)
			{
				// A synchronised type is allowed to read the viewport from a RENDER entry point:
				// that code runs on the local client to decide what to draw, and its result never
				// re-enters the simulation. FrozenActorLayer.Render and WeatherOverlay's
				// IRenderAboveWorld both do exactly this. Anything OUTSIDE such a method is the
				// hazard, so those are resolved through the interface map rather than by name.
				var renderEntryPoints = RenderEntryPoints(type);
				renderMethods += renderEntryPoints.Count;

				foreach (var method in type
					.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
						BindingFlags.Static | BindingFlags.DeclaredOnly)
					.OfType<MethodBase>())
				{
					if (method is MethodInfo mi && renderEntryPoints.Contains(mi))
						continue;

					var scan = IlScan.Scan(method);
					resolvedCalls += scan.ResolvedCalls;

					foreach (var callee in scan.Callees)
					{
						if (callee.DeclaringType == typeof(Viewport) &&
							viewportMembers.ContainsKey(callee.MetadataToken))
						{
							offenders.Add($"{type.FullName}.{method.Name} -> Viewport.{callee.Name}");
							break;
						}
					}
				}
			}

			Assert.That(resolvedCalls, Is.GreaterThan(500),
				$"IL scan resolved only {resolvedCalls} call targets — the scanner is broken, not the code clean.");
			Assert.That(renderMethods, Is.GreaterThan(20),
				$"Only {renderMethods} render entry points resolved — the exemption above is over-broad " +
				"or the interface map lookup is failing, either of which hides real offenders.");

			Assert.That(offenders, Is.Empty,
				"These synchronised types read the viewport outside a render entry point. The viewport " +
				"is per-client view state and ScreenShaker moves it by a client-local amount, so " +
				"anything sync-hashed that depends on it will desync the moment two players are " +
				"looking at different places:" +
				Environment.NewLine + string.Join(Environment.NewLine, offenders));
		}

		/// <summary>Methods on <paramref name="type"/> that implement a member of some IRender* interface.</summary>
		static HashSet<MethodInfo> RenderEntryPoints(Type type)
		{
			var set = new HashSet<MethodInfo>();
			if (type.IsInterface)
				return set;

			foreach (var iface in type.GetInterfaces())
			{
				if (!iface.Name.StartsWith("IRender", StringComparison.Ordinal))
					continue;

				var map = type.GetInterfaceMap(iface);
				foreach (var m in map.TargetMethods)
					set.Add(m);
			}

			return set;
		}

		[Test]
		public void ScreenShakerTouchesNothingButTheViewport()
		{
			// The other half of the claim: the shaker itself must not reach back into the simulation.
			//
			// The obvious formulation — "does it read World.SharedRandom" — is not scannable, because
			// SharedRandom is a FIELD (World.cs:50) and an IL call scan only sees calls. Checking for
			// the RNG TYPES instead is both scannable and strictly stronger: no MersenneTwister and no
			// System.Random may be touched at all, whichever instance it came from.
			var rngTypes = new[] { typeof(OpenRA.Support.MersenneTwister), typeof(Random) };

			var scanned = new[] { typeof(ScreenShaker), typeof(ScreenShakeModel), typeof(ShakeEffect) };
			var resolvedCalls = 0;
			var offenders = new List<string>();

			foreach (var type in scanned)
			{
				foreach (var method in type
					.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
						BindingFlags.Static | BindingFlags.DeclaredOnly)
					.OfType<MethodBase>())
				{
					var scan = IlScan.Scan(method);
					resolvedCalls += scan.ResolvedCalls;

					foreach (var callee in scan.Callees)
						if (rngTypes.Contains(callee.DeclaringType))
							offenders.Add($"{type.FullName}.{method.Name} -> {callee.DeclaringType.Name}.{callee.Name}");
				}
			}

			Assert.That(resolvedCalls, Is.GreaterThan(20),
				$"IL scan resolved only {resolvedCalls} call targets — the scanner is broken, not the code clean.");

			Assert.That(offenders, Is.Empty,
				"The screen shaker draws from a random number generator. If that is World.SharedRandom " +
				"it is synchronised across every client, so consuming it to seed a cosmetic waveform " +
				"advances it out of step and desyncs the match. Use ShakeEffect.Hash, which is a pure " +
				"function of position and spawn tick:" +
				Environment.NewLine + string.Join(Environment.NewLine, offenders));
		}

		[Test]
		public void NoSynchronisedTypeReadsTheScreenShaker()
		{
			// The third side of the same claim, and the one that was unguarded until 2026-09-07.
			// ScreenShakerTouchesNothingButTheViewport stops the shaker reaching INTO the simulation;
			// this stops the simulation reading OUT of it.
			//
			// It matters more than it did: the shaker now carries per-client state that a synced type
			// could sample — the sub-pixel quantisation residual, which depends on the local player's
			// zoom, and the live effects list, which depends on which effects this client has spawned.
			// Both are client-local by construction, so a sync-hashed trait reading either would
			// desync the moment two players were zoomed differently.
			var shakerTypes = new[] { typeof(ScreenShaker), typeof(ScreenShakeModel), typeof(ShakeEffect) };
			var members = shakerTypes
				.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
					BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
				.ToDictionary(m => m.MetadataToken, m => m);

			Assert.That(members.Count, Is.GreaterThan(10),
				"Found almost no ScreenShaker members — this test no longer scans what it claims to.");

			// AddEffect is how the simulation is MEANT to talk to the shaker: it hands over a
			// position and some parameters, returns nothing, and reads nothing back. A synced trait
			// calling it observes no client-local state, so it is not a hazard. Everything else is.
			var writeOnly = new[] { "AddEffect" };

			var syncTypes = new[] { typeof(Viewport).Assembly, typeof(OpenRA.Mods.Common.Traits.ShakeOnDeath).Assembly }
				.SelectMany(a => a.GetTypes())
				.Where(t => typeof(ISync).IsAssignableFrom(t) && !t.IsInterface)
				.ToArray();

			Assert.That(syncTypes.Length, Is.GreaterThan(50),
				$"Only {syncTypes.Length} ISync types found — the reflection above is wrong, not the code clean.");

			var resolvedCalls = 0;
			var offenders = new List<string>();

			foreach (var type in syncTypes)
			{
				foreach (var method in type
					.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
						BindingFlags.Static | BindingFlags.DeclaredOnly)
					.OfType<MethodBase>())
				{
					var scan = IlScan.Scan(method);
					resolvedCalls += scan.ResolvedCalls;

					foreach (var callee in scan.Callees)
						if (members.ContainsKey(callee.MetadataToken) &&
							shakerTypes.Contains(callee.DeclaringType) &&
							!writeOnly.Contains(callee.Name))
							offenders.Add($"{type.FullName}.{method.Name} -> {callee.DeclaringType.Name}.{callee.Name}");
				}
			}

			Assert.That(resolvedCalls, Is.GreaterThan(500),
				$"IL scan resolved only {resolvedCalls} call targets — the scanner is broken, not the code clean.");

			Assert.That(offenders, Is.Empty,
				"These synchronised types read screen-shake state. It is per-client presentation — the " +
				"quantisation residual follows the local zoom and the effects list follows the local " +
				"camera — so anything sync-hashed that depends on it desyncs the match:" +
				Environment.NewLine + string.Join(Environment.NewLine, offenders));
		}
	}
}
