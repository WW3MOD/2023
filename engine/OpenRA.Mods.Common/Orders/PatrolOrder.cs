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

using System.Collections.Generic;
using System.Text;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Orders
{
	/// <summary>
	/// Wire format and resolution for the "Patrol" order.
	/// The waypoint list rides in <see cref="Order.TargetString"/> because an Order carries only a
	/// single Target, and a patrol route needs N cells. Encoding is "x,y,x,y,…" — the same shape
	/// FieldLoader uses for CPos[] — parsed with invariant integer parsing so the route decodes
	/// byte-identically regardless of the receiving client's culture.
	/// </summary>
	public static class PatrolOrder
	{
		public const string OrderString = "Patrol";

		public static string SerializeWaypoints(IReadOnlyList<CPos> waypoints)
		{
			var sb = new StringBuilder();
			for (var i = 0; i < waypoints.Count; i++)
			{
				if (i > 0)
					sb.Append(',');

				sb.Append(waypoints[i].X.ToStringInvariant());
				sb.Append(',');
				sb.Append(waypoints[i].Y.ToStringInvariant());
			}

			return sb.ToString();
		}

		/// <summary>Decodes a waypoint list, or returns null if the string is malformed.</summary>
		public static CPos[] DeserializeWaypoints(string encoded)
		{
			if (string.IsNullOrEmpty(encoded))
				return null;

			var parts = encoded.Split(',');
			if (parts.Length < 2 || parts.Length % 2 != 0)
				return null;

			var waypoints = new CPos[parts.Length / 2];
			for (var i = 0; i < waypoints.Length; i++)
			{
				if (!Exts.TryParseInt32Invariant(parts[2 * i], out var x) ||
					!Exts.TryParseInt32Invariant(parts[2 * i + 1], out var y))
					return null;

				waypoints[i] = new CPos(x, y);
			}

			return waypoints;
		}

		/// <summary>
		/// Applies a resolved "Patrol" order. Called from every IMove trait that can receive one, so
		/// that every client queues the same PatrolActivity from the same replicated waypoint list.
		/// </summary>
		public static void Resolve(Actor self, Order order)
		{
			var waypoints = DeserializeWaypoints(order.TargetString);
			if (waypoints == null || waypoints.Length < 2)
				return;

			// The route arrives from another client, so bound it before it reaches the pathfinder.
			var map = self.World.Map;
			for (var i = 0; i < waypoints.Length; i++)
				waypoints[i] = map.Clamp(waypoints[i]);

			self.QueueActivity(order.Queued, new PatrolActivity(self, waypoints));
			self.ShowTargetLines();
		}
	}
}
