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

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BeaconLib;
using OpenRA.Network;
using OpenRA.Server;
using OpenRA.Support;
using S = OpenRA.Server.Server;

namespace OpenRA.Mods.Common.Server
{
	public class MasterServerPinger : ServerTrait, ITick, INotifyServerStart, INotifyServerShutdown, INotifySyncLobbyInfo, IStartGame, IEndGame
	{
		// 3 minutes (in milliseconds). Server has a 5 minute TTL for games, so give ourselves a bit of leeway.
		const int MasterPingInterval = 60 * 3 * 1000;

		// 1 second (in milliseconds) minimum delay between pings
		const int RateLimitInterval = 1000;

		[FluentReference]
		const string NoPortForward = "notification-no-port-forward";

		[FluentReference]
		const string NoPortForwardDiscoveryDisabled = "notification-no-port-forward-discovery-disabled";

		[FluentReference]
		const string NoPortForwardNoDevice = "notification-no-port-forward-no-device";

		[FluentReference]
		const string NoPortForwardRejected = "notification-no-port-forward-rejected";

		[FluentReference]
		const string NoPortForwardUpstream = "notification-no-port-forward-upstream";

		[FluentReference("address")]
		const string NoPortForwardCarrierGradeNat = "notification-no-port-forward-carrier-grade-nat";

		[FluentReference("endpoint")]
		const string NoPortForwardLocalAddress = "notification-no-port-forward-local-address";

		[FluentReference]
		const string BlacklistedTitle = "notification-blacklisted-server-name";

		[FluentReference]
		const string InvalidErrorCode = "notification-invalid-error-code";

		[FluentReference]
		const string Connected = "notification-master-server-connected";

		[FluentReference]
		const string Error = "notification-master-server-error";

		[FluentReference]
		const string GameOffline = "notification-game-offline";

		static readonly Beacon LanGameBeacon;

		// Code 1 is handled separately: it is the one error whose real cause is already known locally,
		// so it is expanded into the failing step rather than mapped to a single string.
		static readonly Dictionary<int, string> MasterServerErrors = new()
		{
			{ 2, BlacklistedTitle }
		};

		long lastPing = 0;
		long lastChanged = 0;
		bool isInitialPing = true;

		volatile bool isBusy;
		readonly Queue<(string Key, object[] Args)> masterServerMessages = new();

		static MasterServerPinger()
		{
			try
			{
				LanGameBeacon = new Beacon("OpenRALANGame", (ushort)new Random(DateTime.Now.Millisecond).Next(2048, 60000));
			}
			catch (Exception ex)
			{
				Log.Write("server", "BeaconLib.Beacon: " + ex.Message);
			}
		}

		public void Tick(S server)
		{
			// Force an update if the last one was too long ago so the advertisement doesn't time out
			if (Game.RunTime - lastChanged > MasterPingInterval)
				lastChanged = Game.RunTime;

			// Update the master server and LAN clients if something has changed
			// Note that isBusy is set while the master server ping is running on a
			// background thread, and limits LAN pings as well as master server pings for simplicity.
			if (!isBusy && ((lastChanged > lastPing && Game.RunTime - lastPing > RateLimitInterval) || isInitialPing))
			{
				var gs = new GameServer(server);
				if (server.Settings.AdvertiseOnline)
					UpdateMasterServer(server, gs.ToPOSTData(false));

				if (LanGameBeacon != null)
					LanGameBeacon.BeaconData = gs.ToPOSTData(true);

				lastPing = Game.RunTime;
			}

			lock (masterServerMessages)
			{
				while (masterServerMessages.Count > 0)
				{
					var (key, args) = masterServerMessages.Dequeue();
					server.SendFluentMessage(key, args);
				}
			}
		}

		void INotifyServerStart.ServerStarted(S server)
		{
			if (server.IsMultiplayer && LanGameBeacon != null)
				LanGameBeacon.Start();
		}

		void INotifyServerShutdown.ServerShutdown(S server)
		{
			if (server.Settings.AdvertiseOnline)
			{
				// Announce that the game has ended to remove it from the list.
				var gameServer = new GameServer(server);
				UpdateMasterServer(server, gameServer.ToPOSTData(false));
			}

			LanGameBeacon?.Stop();
		}

		public void LobbyInfoSynced(S server)
		{
			lastChanged = Game.RunTime;
		}

		public void GameStarted(S server)
		{
			lastChanged = Game.RunTime;
		}

		public void GameEnded(S server)
		{
			LanGameBeacon?.Stop();

			lastChanged = Game.RunTime;
		}

		/// <summary>
		/// Expands master server error code 1 ("could not reach your port") into the step that actually
		/// failed. The caller must hold the <see cref="masterServerMessages"/> lock.
		/// </summary>
		void EnqueuePortForwardDiagnosis(S server)
		{
			var localAddress = NetworkDiagnostics.GetLocalAddress();

			// A second layer of NAT at the ISP outranks whatever the local router did, because no rule
			// on that router can expose this port either way. Only knowable when a device was found.
			if (NetworkDiagnostics.IsPrivate(localAddress) && NetworkDiagnostics.IsCarrierGradeNat(Nat.ExternalAddress))
			{
				masterServerMessages.Enqueue((NoPortForwardCarrierGradeNat,
					new object[] { "address", Nat.ExternalAddress.ToString() }));

				return;
			}

			var cause = Nat.ForwardStatus switch
			{
				NatForwardStatus.DiscoveryDisabled => NoPortForwardDiscoveryDisabled,
				NatForwardStatus.NoDeviceFound => NoPortForwardNoDevice,
				NatForwardStatus.DeviceRejected => NoPortForwardRejected,
				NatForwardStatus.Forwarded => NoPortForwardUpstream,
				_ => NoPortForward
			};

			masterServerMessages.Enqueue((cause, Array.Empty<object>()));

			// The single most useful line: a port-forward rule aimed at an address this machine no
			// longer holds fails in exactly the same way as having no rule at all.
			if (localAddress != null)
				masterServerMessages.Enqueue((NoPortForwardLocalAddress,
					new object[] { "endpoint", $"{localAddress}:{server.Settings.ListenPort}" }));
		}

		void UpdateMasterServer(S server, string postData)
		{
			isBusy = true;

			Task.Run(async () =>
			{
				try
				{
					var endpoint = server.ModData.Manifest.Get<WebServices>().ServerAdvertise;

					var client = HttpClientFactory.Create();
					var response = await client.PostAsync(endpoint, new StringContent(postData));

					var masterResponseText = await response.Content.ReadAsStringAsync();

					if (isInitialPing)
					{
						Log.Write("server", "Master server: " + masterResponseText);
						var errorCode = 0;
						var errorMessage = string.Empty;

						if (!string.IsNullOrWhiteSpace(masterResponseText))
						{
							var regex = new Regex(@"^\[(?<code>-?\d+)\](?<message>.*)");
							var match = regex.Match(masterResponseText);
							errorMessage = match.Success && int.TryParse(match.Groups["code"].Value, out errorCode) ?
								match.Groups["message"].Value.Trim() : InvalidErrorCode;
						}

						isInitialPing = false;
						lock (masterServerMessages)
						{
							masterServerMessages.Enqueue((Connected, Array.Empty<object>()));
							if (errorCode != 0)
							{
								if (errorCode == 1)
									EnqueuePortForwardDiagnosis(server);
								else
								{
									// Hardcoded error messages take precedence over the server-provided messages
									if (!MasterServerErrors.TryGetValue(errorCode, out var message))
										message = errorMessage;

									masterServerMessages.Enqueue((message, Array.Empty<object>()));
								}

								// Positive error codes indicate errors that prevent advertisement
								// Negative error codes are non-fatal warnings
								if (errorCode > 0)
									masterServerMessages.Enqueue((GameOffline, Array.Empty<object>()));
							}
						}
					}
				}
				catch (Exception ex)
				{
					Log.Write("server", ex.ToString());
					lock (masterServerMessages)
						masterServerMessages.Enqueue((Error, Array.Empty<object>()));
				}

				isBusy = false;
			});
		}
	}
}
