#region Copyright & License Information
/*
 * WW3MOD addition. Not upstream OpenRA.
 *
 * Distributed under the same terms as the rest of the engine: GNU General Public License
 * version 3 or (at your option) any later version. See COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Primitives;

namespace OpenRA.Network
{
	/// <summary>
	/// Seats bot clients into a client-side <see cref="Session"/> so that a map whose players are
	/// Playable can run as a shellmap. WW3MOD-only: upstream never writes into a client-side
	/// session, so upstream never has to defend this invariant.
	/// </summary>
	public static class ShellmapLobby
	{
		/// <summary>
		/// Replaces whatever is currently seated in <paramref name="lobbyInfo"/> with one bot per
		/// playable player. Safe to call repeatedly against the same session.
		/// </summary>
		public static void SeatBots(
			Session lobbyInfo,
			IEnumerable<(string SlotKey, string Name, string Faction, Color Color, int Team)> playablePlayers,
			string botType,
			int botControllerClientIndex)
		{
			// Rebuild rather than append. Session.ClientInSlot uses SingleOrDefault, so a second
			// append for a slot key both maps share throws during world creation; and a slot left
			// behind by a map that is no longer loading throws in CreateMapPlayers, which indexes
			// the new map's players by the stale slot's PlayerReference.
			lobbyInfo.Clients.RemoveAll(c => c.Slot != null);
			lobbyInfo.Slots.Clear();

			var nextClientIndex = lobbyInfo.Clients.Count > 0
				? lobbyInfo.Clients.Max(c => c.Index) + 1
				: 1;

			foreach (var player in playablePlayers)
			{
				lobbyInfo.Slots[player.SlotKey] = new Session.Slot
				{
					PlayerReference = player.SlotKey,
					AllowBots = true,
					LockFaction = true,
					LockColor = true,
					LockTeam = true,
					LockSpawn = true,
				};

				lobbyInfo.Clients.Add(new Session.Client
				{
					Index = nextClientIndex++,
					Bot = botType,
					BotControllerClientIndex = botControllerClientIndex,
					Name = player.Name,
					Faction = player.Faction,
					Color = player.Color,
					Team = player.Team,
					Slot = player.SlotKey,
					State = Session.ClientState.Ready,
				});
			}
		}
	}
}
