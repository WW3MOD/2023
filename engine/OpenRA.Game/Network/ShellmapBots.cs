#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 *
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Network
{
	public static class ShellmapBots
	{
		/// <summary>
		/// Seats <paramref name="bots"/> into <paramref name="lobbyInfo"/>, replacing rather than
		/// appending. <see cref="Session.ClientInSlot"/> uses SingleOrDefault, so a second client
		/// carrying an already-taken slot key throws during world creation rather than losing a
		/// tiebreak — see WORKSPACE/audit/260816-crash-clientinslot.md.
		/// </summary>
		public static void SeatBots(Session lobbyInfo, IReadOnlyCollection<Session.Client> bots, int botControllerClientIndex)
		{
			var slotKeys = bots.Select(b => b.Slot).ToHashSet();

			// Two removals, not one: the first keeps the slots we are about to fill single-occupancy,
			// the second evicts bots we seated for a map whose slot keys the new map does not share.
			lobbyInfo.Clients.RemoveAll(c => c.Slot != null
				&& (slotKeys.Contains(c.Slot)
					|| (c.Bot != null && c.BotControllerClientIndex == botControllerClientIndex)));

			var nextClientIndex = lobbyInfo.Clients.Count > 0 ? lobbyInfo.Clients.Max(c => c.Index) + 1 : 1;
			foreach (var bot in bots)
			{
				bot.Index = nextClientIndex++;
				lobbyInfo.Clients.Add(bot);
			}
		}
	}
}
