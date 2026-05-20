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

using OpenRA.Network;

namespace OpenRA.Server
{
	// Returns true if order is handled
	public interface IInterpretCommand { bool InterpretCommand(Server server, Connection conn, Session.Client client, string cmd); }
	public interface INotifySyncLobbyInfo { void LobbyInfoSynced(Server server); }
	public interface INotifyServerStart { void ServerStarted(Server server); }
	public interface INotifyServerEmpty { void ServerEmpty(Server server); }
	public interface INotifyServerShutdown { void ServerShutdown(Server server); }
	public interface IStartGame { void GameStarted(Server server); }
	public interface IClientJoined { void ClientJoined(Server server, Connection conn); }
	public interface IEndGame { void GameEnded(Server server); }
	public interface ITick { void Tick(Server server); }

	public abstract class ServerTrait { }

	public class DebugServerTrait : ServerTrait, IInterpretCommand, IStartGame, INotifySyncLobbyInfo, INotifyServerStart, INotifyServerShutdown, IEndGame
	{
		public bool InterpretCommand(Server server, Connection conn, Session.Client client, string cmd)
		{
			Log.Write("server", $"Server received command from player {conn.PlayerIndex}: {cmd}");
			return false;
		}

		public void GameStarted(Server server)
		{
			Log.Write("server", "GameStarted()");
		}

		public void LobbyInfoSynced(Server server)
		{
			Log.Write("server", "LobbyInfoSynced()");
		}

		public void ServerStarted(Server server)
		{
			Log.Write("server", "ServerStarted()");
		}

		public void ServerShutdown(Server server)
		{
			Log.Write("server", "ServerShutdown()");
		}

		public void GameEnded(Server server)
		{
			Log.Write("server", "GameEnded()");
		}
	}
}
