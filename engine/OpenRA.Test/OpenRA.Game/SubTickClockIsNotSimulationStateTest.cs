#region Copyright & License Information
/*
 * WW3MOD sub-tick clock / simulation separation guard (2026-09-06).
 *
 * SubTickClock.Fraction is derived from Game.RunTime, a Stopwatch. Two clients running the same
 * match read DIFFERENT fractions on the same tick — that is the entire point of it, and it is why a
 * synchronised trait reading it desyncs the match. The failure would not show up in single-player,
 * and would not show up in a two-client test either until the clients happened to drift, so it is
 * pinned structurally instead.
 *
 * ISync is OpenRA's own marker for "this participates in the sync hash" (Actor.cs tests for it
 * before hashing anything), so "no ISync implementor reads the sub-tick clock" is the claim stated
 * in the engine's own vocabulary rather than in a hand-maintained list of type names.
 *
 * WHY THERE IS AN EXEMPTION AT ALL, and why it is narrower than its sibling's. Every
 * ConditionalTrait<T> is ISync (Traits/Conditions/ConditionalTrait.cs:41) for its IsTraitDisabled
 * flag, so the only consumer of this clock is unavoidably an ISync type — "zero ISync readers" is
 * not a reachable bar for any trait that wants a RequiresCondition. So this follows
 * ViewportIsNotSimulationStateTest and exempts IRender* entry points, whose results never re-enter
 * the simulation, and then goes one step further than that fixture does: the exempted readers are
 * pinned to an explicit list, so a SECOND type quietly starting to read the clock fails here even
 * though it read it from a legal place.
 *
 * The case that will hit that list first is Bullet or Missile (Projectiles/Bullet.cs:152,
 * Projectiles/Missile.cs:211) — both are `IProjectile, ISync`, both are fast enough to want
 * smoothing, and neither renders through an IRender* interface: IEffect.Render is not exempt, so
 * adding a read there fails as an offender rather than being absorbed. That is deliberate. Fencing
 * a projectile's render path off from its sync hash is a decision that belongs in front of a human.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SubTickClockIsNotSimulationStateTest
	{
		/// <summary>
		/// ISync types allowed to read the clock, and then only from an IRender* entry point. Adding
		/// to this is the whole review gate — see the file header before you do.
		/// </summary>
		static readonly Type[] PermittedRenderReaders = { typeof(SubTickMotionSmoothing) };

		static readonly Assembly[] Assemblies =
		{
			typeof(SubTickClock).Assembly,
			typeof(SubTickMotionSmoothing).Assembly
		};

		static Dictionary<int, string> ClockMembers()
		{
			var members = typeof(SubTickClock)
				.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
				.Where(m => m.DeclaringType == typeof(SubTickClock))
				.ToDictionary(m => m.MetadataToken, m => m.Name);

			// Fraction's getter and private setter, Measure, Update, Reset. If a refactor leaves this
			// near zero, every scan below passes by scanning for nothing.
			Assert.That(members.Count, Is.GreaterThan(3),
				$"Found only {members.Count} SubTickClock members — this test no longer scans what it claims to.");

			return members;
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

				foreach (var m in type.GetInterfaceMap(iface).TargetMethods)
					set.Add(m);
			}

			return set;
		}

		[Test]
		public void NoSynchronisedTypeReadsTheSubTickClockOutsideARenderPath()
		{
			var clockMembers = ClockMembers();

			var syncTypes = Assemblies
				.SelectMany(a => a.GetTypes())
				.Where(t => typeof(ISync).IsAssignableFrom(t) && !t.IsInterface)
				.ToArray();

			Assert.That(syncTypes.Length, Is.GreaterThan(50),
				$"Only {syncTypes.Length} ISync types found — the reflection above is wrong, not the code clean.");

			var resolvedCalls = 0;
			var renderMethods = 0;
			var offenders = new List<string>();
			var renderReaders = new HashSet<Type>();

			foreach (var type in syncTypes)
			{
				var renderEntryPoints = RenderEntryPoints(type);
				renderMethods += renderEntryPoints.Count;

				foreach (var method in type
					.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
						BindingFlags.Static | BindingFlags.DeclaredOnly)
					.OfType<MethodBase>())
				{
					var scan = IlScan.Scan(method);
					resolvedCalls += scan.ResolvedCalls;

					var reads = scan.Callees.Any(c =>
						c.DeclaringType == typeof(SubTickClock) && clockMembers.ContainsKey(c.MetadataToken));

					if (!reads)
						continue;

					if (method is MethodInfo mi && renderEntryPoints.Contains(mi))
						renderReaders.Add(type);
					else
						offenders.Add($"{type.FullName}.{method.Name}");
				}
			}

			Assert.That(resolvedCalls, Is.GreaterThan(500),
				$"IL scan resolved only {resolvedCalls} call targets — the scanner is broken, not the code clean.");
			Assert.That(renderMethods, Is.GreaterThan(20),
				$"Only {renderMethods} render entry points resolved — the exemption is over-broad or the " +
				"interface map lookup is failing, either of which hides real offenders.");

			Assert.That(offenders, Is.Empty,
				"These synchronised types read the sub-tick clock from outside a render entry point. It " +
				"is a wall-clock quantity: two clients read different values on the same tick, so " +
				"anything sync-hashed that depends on it desyncs the match, silently and only in " +
				"multiplayer. Consume it into a renderable and return, the way SubTickMotionSmoothing " +
				"does, rather than letting it reach a field:" +
				Environment.NewLine + string.Join(Environment.NewLine, offenders));

			// The exemption is not open house. A new reader is legal only once someone has looked at it.
			var unexpected = renderReaders.Except(PermittedRenderReaders).Select(t => t.FullName).ToArray();
			Assert.That(unexpected, Is.Empty,
				"These synchronised types read the sub-tick clock from a render entry point, which is " +
				"the legal place, but they are not on PermittedRenderReaders. Read this file's header, " +
				"satisfy yourself that the value cannot reach the type's synced state, and add them:" +
				Environment.NewLine + string.Join(Environment.NewLine, unexpected));

			// And it is not a dead list either: if the one permitted reader stops reading, the whole
			// feature has been refactored away and this fixture is guarding nothing.
			Assert.That(renderReaders, Is.Not.Empty,
				"No type reads the sub-tick clock from a render path any more — the smoothing feature " +
				"is gone, and every assertion above now passes vacuously.");
		}

		[Test]
		public void TheSmoothingTraitReadsTheClockOnlyFromItsRenderPath()
		{
			// The narrower claim about the one permitted reader, and the one that actually makes it
			// safe: the read happens in ModifyRender, which returns renderables, and NOT in
			// ITick.Tick, which is the simulation path. If the read migrates into a shared helper that
			// Tick can also call, this fails even though the type is still on the permitted list.
			var clockMembers = ClockMembers();
			var renderEntryPoints = RenderEntryPoints(typeof(SubTickMotionSmoothing));

			Assert.That(renderEntryPoints, Is.Not.Empty,
				"No IRender* entry points resolved on SubTickMotionSmoothing — the interface map lookup is failing.");

			var resolvedCalls = 0;
			var readers = new List<string>();
			var offenders = new List<string>();

			foreach (var method in typeof(SubTickMotionSmoothing)
				.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
					BindingFlags.Static | BindingFlags.DeclaredOnly)
				.OfType<MethodBase>())
			{
				var scan = IlScan.Scan(method);
				resolvedCalls += scan.ResolvedCalls;

				if (!scan.Callees.Any(c =>
					c.DeclaringType == typeof(SubTickClock) && clockMembers.ContainsKey(c.MetadataToken)))
					continue;

				readers.Add(method.Name);
				if (!(method is MethodInfo mi && renderEntryPoints.Contains(mi)))
					offenders.Add(method.Name);
			}

			// Floor sized to the trait, which is deliberately small: a ctor, a Tick, two interface
			// methods. It resolved 14 when written. This only has to catch "resolved nothing".
			Assert.That(resolvedCalls, Is.GreaterThan(5),
				$"IL scan resolved only {resolvedCalls} call targets — the scanner is broken, not the code clean.");

			Assert.That(readers, Is.Not.Empty,
				"Nothing in SubTickMotionSmoothing reads SubTickClock any more — the trait no longer does its job.");

			Assert.That(offenders, Is.Empty,
				"These SubTickMotionSmoothing methods read the sub-tick clock from outside its render " +
				"path: " + string.Join(", ", offenders));
		}

		[Test]
		public void TheSmoothingTraitSyncsNothingOfItsOwn()
		{
			// The trait is ISync only because ConditionalTrait<T> is, for IsTraitDisabled. Its own
			// state — a position and a velocity — must stay out of the hash: Hovers, the trait this
			// one is modelled on, marks its visual offset [Sync], and copying that here would put the
			// value the wall-clock fraction multiplies into the sync hash.
			var synced = typeof(SubTickMotionSmoothing)
				.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
				.Where(f => f.GetCustomAttributes(typeof(SyncAttribute), true).Length > 0)
				.Select(f => f.Name)
				.ToArray();

			Assert.That(synced, Is.Empty,
				"Fields marked [Sync] on SubTickMotionSmoothing: " + string.Join(", ", synced));
		}
	}
}
