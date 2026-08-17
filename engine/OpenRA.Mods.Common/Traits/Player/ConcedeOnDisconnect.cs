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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Defeats a player whose client has been dropped, so an abandoned match can end.",
		"Without this the dropped player is never marked Lost: the abandoned army freezes in place,",
		"keeps auto-defending, and the survivor's only route to a win is manually force-attacking a",
		"75,000 HP NoAutoTarget Supply Route. In practice the match simply never ends.")]
	public class ConcedeOnDisconnectInfo : TraitInfo, Requires<MissionObjectivesInfo>
	{
		public override object Create(ActorInitializer init) { return new ConcedeOnDisconnect(init.Self); }
	}

	// DETERMINISM: this mutates synced state (WinState is packed into the defeat mask that
	// OrderManager.ProcessOrders sends with every sync hash, and MissionObjectives.ObjectivesHash is
	// [Sync]), so it must run on the same frame on every remaining client or it desyncs the game it
	// is trying to end. It does. The server broadcasts one Disconnect packet stamped
	// LastOrdersFrame + 1 to all clients (Server.DropClient), which OrderManager.ReceiveDisconnect
	// inserts into the order stream and ProcessOrders unpacks at exactly that NetFrameNumber. That is
	// what INotifyPlayerDisconnected exists for; nothing here reads wall-clock time or RNG.
	//
	// It also cannot fire on a hiccup: the only paths to DropClient are an explicitly closed
	// connection and PlayerPinger's 60 s unresponsive timeout, and the lockstep loop stalls rather
	// than drops for anything shorter.
	public class ConcedeOnDisconnect : INotifyPlayerDisconnected
	{
		[FluentReference("player")]
		const string PlayerConceded = "notification-player-conceded-disconnect";

		readonly MissionObjectives mo;

		public ConcedeOnDisconnect(Actor self)
		{
			mo = self.Trait<MissionObjectives>();
		}

		void INotifyPlayerDisconnected.PlayerDisconnected(Actor self, Player p)
		{
			// Dispatched to every player's actor with the dropped player as the argument
			// (World.OnClientDisconnected -> Player.PlayerDisconnected), so only act on our own.
			if (p != self.Owner || p.WinState != WinState.Undefined)
				return;

			// The same defeat a surrender produces (PlayerCommands "Surrender" -> ForceDefeat), so the
			// objectives panel, the win award to the survivor and the end-of-match screen all behave
			// exactly as they do for a player who quit deliberately.
			mo.ForceDefeat(p);

			// ForceDefeat can only fail objectives that already exist, and ConquestVictoryConditions
			// adds its primary objective lazily on the owner's first tick. ProcessOrders runs before
			// the world ticks, so a client dropped on net frame 1 hits an empty objective list and
			// ForceDefeat silently no-ops - leaving exactly the never-ending match this trait exists
			// to prevent. Add one and fail it, the way SupplyRouteContestation.ResolveTeamElimination
			// does, so the defeat still runs through MarkFailed and reaches CheckIfGameIsOver.
			if (p.WinState == WinState.Undefined)
				mo.MarkFailed(p, mo.Add(p, "Stay in the game", "Primary", inhibitAnnouncement: true));

			TextNotificationsManager.AddSystemLine(PlayerConceded, "player", p.ResolvedPlayerName);
		}
	}
}
