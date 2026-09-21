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

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Scripting;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// `Cargo.Load` adds a man to the hold and does NOT take him out of the world — the removal is
	/// the caller's half of the pair, and every other caller in the engine does it. The Lua binding
	/// did not, so an in-world actor handed to `Transport.LoadPassenger` ended up in the hold AND on
	/// the map, and the crash arrived arbitrarily later at whatever eventually unloaded him: both
	/// `UnloadCargo` and `GarrisonManager.DeployToPort` finish in `World.Add`, which is an unguarded
	/// `actors.Add(a.ActorID, a)` and throws `An item with the same key has already been added` from
	/// inside a frame-end task — with not one frame of the offending Lua in the trace.
	///
	/// <para>Measured: run 260921_162312 killed demo-garrison-lineup 95 s in, out of DeployToPort, on
	/// a rifleman loaded ninety seconds earlier. The trap predates that run — test-field-heli-unload
	/// and test-unload-queued-after-waypoints each carry a PITFALL comment quoting the same exception
	/// string and work around it with `Actor.Create(type, false, ...)`.</para>
	///
	/// <para>No autotest can pin this. A scenario proves the binding works on the actors IT passes;
	/// the defect is about the actors it does NOT pass, and reproducing it costs a launch and a
	/// ninety-second wait for a crash in another file. So the pairing is asserted structurally.</para>
	/// </summary>
	[TestFixture]
	public class LoadPassengerWorldStateTest
	{
		[Test]
		public void LoadPassengerAlsoRemovesTheManFromTheWorld()
		{
			// The removal is necessarily inside a frame-end closure -- Lua runs in the world tick and
			// World.Remove mutates the dictionary being iterated -- so the compiler hoists it into a
			// nested display class. Scanning the method alone would find nothing and read as clean,
			// which is the one failure mode IlScan's header warns about; the nested types are where
			// the call actually lives.
			var type = typeof(TransportProperties);
			var bodies = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				.Where(m => m.Name == nameof(TransportProperties.LoadPassenger))
				.Cast<MethodBase>()
				.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
					.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
						| BindingFlags.Instance | BindingFlags.Static))
					.Where(m => m.Name.Contains(nameof(TransportProperties.LoadPassenger)))
					.Cast<MethodBase>())
				.ToArray();

			Assert.That(bodies, Is.Not.Empty, "TransportProperties.LoadPassenger no longer exists under that name.");

			var scans = bodies.Select(IlScan.Scan).ToArray();

			Assert.That(scans.Sum(s => s.ResolvedCalls), Is.GreaterThan(1),
				"the IL scan resolved almost nothing across LoadPassenger and its closures — the " +
				"scanner is broken, not the code clean. Read nothing into the assertions below.");

			var callees = scans.SelectMany(s => s.Callees).ToArray();

			Assert.That(callees.Any(c => c.Name == nameof(Cargo.Load)), Is.True,
				"LoadPassenger no longer calls Cargo.Load, so this fixture is asserting the pairing of " +
				"something else.");

			Assert.That(callees.Any(c => c.DeclaringType == typeof(World) && c.Name == nameof(World.Remove)), Is.True,
				"LoadPassenger adds the passenger to the hold without removing him from the world. He is " +
				"then in both places, and the next thing that unloads him — UnloadCargo, or " +
				"GarrisonManager.DeployToPort — calls World.Add on an actor already in the actor " +
				"dictionary and takes the whole match down with 'An item with the same key has already " +
				"been added', from a frame-end task that names none of the code responsible.");
		}
	}
}
