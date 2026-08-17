#region Copyright & License Information
/*
 * WW3MOD sync-annotation guard.
 *
 * Two failure modes, both of which have already cost this project real debugging time:
 *
 *  1. A [Sync] member of a type the hasher cannot handle. Hash functions are IL-emitted at RUNTIME
 *     (Sync.GenerateHashFunc), so an enum or a double annotated [Sync] compiles perfectly and throws
 *     the first time the game hashes that trait. Nothing else in the test suite would catch it.
 *
 *  2. [Sync] members on a trait that does not implement ISync. Actor.cs only hashes a trait when
 *     `trait is ISync`, so those annotations are INERT — the fields are absent from every sync report
 *     while reading as though they were covered. That is worse than no annotation, because a matching
 *     report then looks exculpatory when it never examined the field at all.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SyncAnnotationTest
	{
		static IEnumerable<Type> ModTypes => typeof(AutoTarget).Assembly.GetTypes()
			.Where(t => t.IsClass && !t.IsAbstract);

		static bool HasSyncMember(Type t)
		{
			const BindingFlags Binding = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			return t.GetFields(Binding).Any(f => f.HasAttribute<SyncAttribute>())
				|| t.GetProperties(Binding).Any(p => p.HasAttribute<SyncAttribute>());
		}

		[Test]
		public void EverySyncTypeCanGenerateItsHashFunction()
		{
			var generate = typeof(Sync).GetMethod("GenerateHashFunc", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.That(generate, Is.Not.Null, "Sync.GenerateHashFunc not found — this guard needs updating.");

			// Non-vacuity: both assertions below are "this collection is empty", which passes for free
			// if the enumeration found nothing. Prove it found something first.
			var syncTypes = ModTypes.Where(t => typeof(ISync).IsAssignableFrom(t)).ToArray();
			Assert.That(syncTypes.Length, Is.GreaterThan(50), "Type enumeration returned almost nothing — this guard is not looking at the mod assembly.");

			var failures = new List<string>();
			foreach (var t in syncTypes)
			{
				try
				{
					generate.Invoke(null, new object[] { t });
				}
				catch (TargetInvocationException e)
				{
					failures.Add($"{t.FullName}: {e.InnerException?.Message}");
				}
			}

			Assert.That(failures, Is.Empty, "A [Sync] member has a type the hasher rejects:\n" + string.Join("\n", failures));
		}

		// Known-dead annotations, pending a separate decision (adding ISync changes the sync hash).
		// This list may SHRINK freely. It must not grow: a new entry means someone wrote [Sync] on a
		// trait that is never hashed, which hides divergence rather than catching it.
		//
		// Now EMPTY. VehicleCrew and SupplyRouteContestation were the last two entries; both gained
		// ISync once replay/save-hash stability was explicitly waived, which was the "separate
		// decision" this list was waiting on. Keeping it empty rather than deleting it is deliberate:
		// the empty array is what makes the assertion below a standing guard instead of a one-off
		// cleanup, so the next trait to acquire an inert [Sync] fails immediately.
		static readonly string[] KnownUnhashedWithSyncMembers = Array.Empty<string>();

		[Test]
		public void NoNewTraitCarriesInertSyncAnnotations()
		{
			// Non-vacuity: prove HasSyncMember actually detects annotations before trusting an empty result.
			Assert.That(HasSyncMember(typeof(AutoTarget)), Is.True, "Sync-member detection is broken.");
			Assert.That(HasSyncMember(typeof(CohesionSlotMemory)), Is.True, "Sync-member detection is broken.");

			var offenders = ModTypes
				.Where(t => !typeof(ISync).IsAssignableFrom(t) && HasSyncMember(t))
				.Select(t => t.FullName)
				.Where(n => !KnownUnhashedWithSyncMembers.Contains(n))
				.OrderBy(n => n, StringComparer.Ordinal)
				.ToArray();

			Assert.That(offenders, Is.Empty,
				"These declare [Sync] members but do not implement ISync, so the annotations are inert:\n"
				+ string.Join("\n", offenders));
		}

		[Test]
		public void AutoTargetHashesTheFourModeFields()
		{
			// These gate CohesionMoveModifier's formation branch and the resupply consumers. They were
			// outside the hash, which is why a matching AutoTarget in a sync report said nothing about
			// stance. Guard them by name so a refactor cannot quietly drop them again.
			const BindingFlags Binding = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			var synced = typeof(AutoTarget).GetProperties(Binding)
				.Where(p => p.HasAttribute<SyncAttribute>())
				.Select(p => p.Name)
				.ToArray();

			Assert.That(synced, Does.Contain("SyncStance"));
			Assert.That(synced, Does.Contain("SyncEngagementStance"));
			Assert.That(synced, Does.Contain("SyncCohesion"));
			Assert.That(synced, Does.Contain("SyncResupplyBehavior"));
		}
	}
}
