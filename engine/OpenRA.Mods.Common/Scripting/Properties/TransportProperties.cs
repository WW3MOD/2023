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
using Eluant;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Scripting;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Scripting
{
	[ScriptPropertyGroup("Transports")]
	public class TransportProperties : ScriptActorProperties, Requires<CargoInfo>
	{
		readonly Cargo cargo;

		public TransportProperties(ScriptContext context, Actor self)
			: base(context, self)
		{
			cargo = self.Trait<Cargo>();
		}

		[Desc("Returns references to passengers inside the transport.")]
		public Actor[] Passengers => cargo.Passengers.ToArray();

		[Desc("Specifies whether transport has any passengers.")]
		public bool HasPassengers => cargo.Passengers.Any();

		[Desc("Specifies the amount of passengers.")]
		public int PassengerCount => cargo.Passengers.Count();

		[Desc("Teleport an existing actor inside this transport.")]
		public void LoadPassenger(Actor a)
		{
			if (!a.IsIdle)
				throw new LuaException("LoadPassenger requires the passenger to be idle.");

			// CanLoad, not a bare Load. Cargo.CanLoad is what consults every ICargoCanLoadFilter on
			// the transport (Cargo.cs:522-533), and going straight to Load walked past all of them --
			// so a Lua map script could seat a passenger that the sim itself would have refused, which
			// for a garrison building means seating an ENEMY. Throwing rather than returning silently
			// because a script asking for an impossible seat has a bug in it, and a silent no-op is how
			// that bug reaches a player instead of the author.
			if (!cargo.CanLoad(a))
				throw new LuaException($"LoadPassenger: {a} cannot be loaded into {Self} — the transport " +
					"refused it (no space, loading blocked, or a cargo filter said no).");

			cargo.Load(Self, a);

			// AND TAKE HIM OUT OF THE WORLD, because Cargo.Load does not and never has.
			//
			// Load only adds to the passenger list; the removal is the CALLER's half of the pair, and
			// every other caller does it -- RideTransport.OnEnterComplete runs `enterCargo.Load(...)`
			// and `w.Remove(self)` in the same frame-end task (:80-81). This binding was the one that
			// did half the job, so a script handing it an IN-WORLD actor got a man who was in the hold
			// AND standing on the map, and the failure surfaced arbitrarily later at the UNLOAD: both
			// UnloadCargo and GarrisonManager.DeployToPort (:450) end in World.Add, which is an
			// unguarded `actors.Add(a.ActorID, a)` (World.cs:395-397) and throws
			// `An item with the same key has already been added` out of a frame-end task, with no
			// frame of the Lua that caused it anywhere in the trace.
			//
			// MEASURED: run 260921_162312 killed demo-garrison-lineup 95 s in, out of DeployToPort,
			// on a rifleman the demo had LoadPassenger'd off the map ninety seconds earlier. The trap
			// was already known -- test-field-heli-unload and test-unload-queued-after-waypoints each
			// carry a PITFALL comment naming this exact exception string and work around it by passing
			// `false` to Actor.Create. A hazard that two scenarios have to remember is a hazard the
			// binding should not have.
			//
			// Deferred rather than immediate: Lua runs inside the world tick and World.Remove mutates
			// the actor dictionary being iterated. A frame-end task is what RideTransport uses, and
			// FIFO drain order means a DeployToPort enqueued later in the same tick still adds AFTER
			// this removes. The `false` idiom keeps working untouched -- an actor created out of world
			// is not IsInWorld, so this is a no-op for it.
			// Pinned by LoadPassengerWorldStateTest.
			if (a.IsInWorld)
				Self.World.AddFrameEndTask(w =>
				{
					if (a.IsInWorld)
						w.Remove(a);
				});
		}

		[Desc("Remove an existing actor (or first actor if none specified) from the transport.  This actor is not added to the world.")]
		public Actor UnloadPassenger(Actor a = null) { return cargo.Unload(Self, a); }

		[ScriptActorPropertyActivity]
		[Desc("Command transport to unload passengers.")]
		public void UnloadPassengers(CPos? cell = null, int unloadRange = 5)
		{
			if (cell.HasValue)
			{
				var destination = Target.FromCell(Self.World, cell.Value);
				Self.QueueActivity(new UnloadCargo(Self, destination, WDist.FromCells(unloadRange)));
			}
			else
				Self.QueueActivity(new UnloadCargo(Self, WDist.FromCells(unloadRange)));
		}
	}
}
