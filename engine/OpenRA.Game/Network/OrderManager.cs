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
using System.IO;
using System.Linq;
using OpenRA.Support;
using OpenRA.Widgets;

namespace OpenRA.Network
{
	public sealed class OrderManager : IDisposable
	{
		const OrderPacket ClientDisconnected = null;

		[FluentReference("frame")]
		const string DesyncCompareLogs = "notification-desync-compare-logs";

		readonly SyncReport syncReport;
		readonly Dictionary<int, Queue<(int Frame, OrderPacket Orders)>> pendingOrders = new();
		readonly Dictionary<int, (int SyncHash, ulong DefeatState)> syncForFrame = new();

		public Session LobbyInfo = new();

		/// <summary>Null when watching a replay.</summary>
		public Session.Client LocalClient => LobbyInfo.ClientWithIndex(Connection.LocalClientId);
		public World World;
		public int OrderQueueLength => pendingOrders.Count > 0 ? pendingOrders.Min(q => q.Value.Count) : 0;

		public string ServerError = null;
		public bool AuthenticationFailed = false;

		// The default null means "no map restriction" while an empty set means "all maps restricted"
		public HashSet<string> ServerMapPool = null;

		public int NetFrameNumber { get; private set; }
		public int LocalFrameNumber;

		public TickTime LastTickTime;

		public bool GameStarted => NetFrameNumber != 0;
		public IConnection Connection { get; }

		internal int GameSaveLastFrame = -1;
		internal int GameSaveLastSyncFrame = -1;

		readonly List<Order> localOrders = new();
		readonly List<Order> localImmediateOrders = new();

		readonly List<ClientOrder> processClientOrders = new();
		readonly List<int> processClientsToRemove = new();

		// DIAGNOSTIC ONLY (Test.SyncHashLog). Per-net-frame trace of the hash this client
		// puts on the wire. Null unless the launch arg asked for it.
		StreamWriter syncHashLog;

		bool disposed;
		bool generateSyncReport = false;
		int sentOrdersFrame = 0;
		float tickScale = 1f;

		/// <summary>
		/// Indicates if the world state of other players or a replay has diverged from the local state.
		/// The game cannot reliably continue in this condition and is unusable.
		/// </summary>
		/// <remarks>Should only be set in <see cref="OutOfSync"/>.</remarks>
		public bool IsOutOfSync { get; private set; } = false;

		/// <summary>The net frame the divergence was detected on. Only meaningful once <see cref="IsOutOfSync"/>.</summary>
		public int OutOfSyncFrame { get; private set; }

		/// <summary>
		/// Absolute path of the sync report written for this desync, or null if no usable report exists.
		/// Only meaningful once <see cref="IsOutOfSync"/>.
		/// </summary>
		public string OutOfSyncReportPath { get; private set; }

		public struct ClientOrder
		{
			public int Client;
			public Order Order;

			public override readonly string ToString()
			{
				return $"ClientId: {Client} {Order}";
			}
		}

		void OutOfSync(int frame)
		{
			if (IsOutOfSync)
				return;

			// Only dump when something was actually recorded. WriteReport decides "we have data for
			// this frame" by matching r.Frame == frame against the ring, but the ring is zero
			// initialised, so with recording off every one of its 32 slots claims to be frame 0. A
			// desync detected AT frame 0 therefore matched all of them and produced a 16KB file of
			// empty records, which DesyncWatcherLogic then asked the player to send. Null here is
			// the signal it needs to say the report is missing instead of naming a useless file.
			OutOfSyncReportPath = generateSyncReport ? syncReport.DumpSyncReport(frame) : null;
			OutOfSyncFrame = frame;
			World.OutOfSync();
			IsOutOfSync = true;

			// Kept for the log and for observers, but it is not the player-facing explanation: this
			// lands in the chat panel that World.OutOfSync has just disabled. DesyncWatcherLogic
			// raises the dialog that actually names the report file.
			TextNotificationsManager.AddSystemLine(DesyncCompareLogs, "frame", frame);
		}

		// Test hook, reached only via World.ForceOutOfSync (Test.ForceDesync). Drives the real desync
		// path rather than faking its symptoms, so a capture proves the whole chain - sync report
		// written, world latched, dialog raised - and not just that a window can be opened.
		// Desyncs on the newest RECORDED frame, not NetFrameNumber: ReceiveSync only ever reports a
		// frame the ring holds, so anything else produces a report reading "No sync report
		// available!" and misrepresents what a real desync looks like.
		internal void ForceOutOfSync()
		{
			OutOfSync(syncReport.LastRecordedFrame);
		}

		public void StartGame()
		{
			if (GameStarted)
				return;

			foreach (var client in LobbyInfo.Clients)
				if (!client.IsBot)
					pendingOrders.Add(client.Index, new Queue<(int, OrderPacket)>());

			// Generating sync reports is expensive, so only do it if we have
			// other players to compare against if a desync did occur.
			// The human-client count is what makes ServerSettings.EnableSyncReports safe to
			// default on: a desync is a disagreement between two peers, so a report is worthless
			// without a second human to diff against. This keeps the whole cost off the bot-vs-bot
			// benchmark and autotest runs, which are timed and run in batches.
			// Test.ForceSyncReports overrides that floor. The floor is right for normal play, but a
			// saved-game restore DOES have a second side to diff against — the recorded match — and
			// it is single-client by construction, so without the override a restore desync reports
			// a frame number and "No sync report available!", which cannot name what diverged.
			// Two separate questions, and either may answer no. "Is there anyone to diff against"
			// is the human-client floor below; "do the players want to pay for it" is the lobby
			// option, which both sides agree on before the match starts. The option falls back to
			// the server setting, so a map carrying no SyncReportsOptionInfo behaves exactly as it
			// did before the option existed. EnableSyncReports stays ANDed on top as a hard
			// ceiling: a dedicated host started with Server.EnableSyncReports=False is not
			// overridden by players ticking the box.
			var humanClients = LobbyInfo.Clients.Count(c => !c.IsBot);
			var reportsWanted = LobbyInfo.GlobalSettings.OptionOrDefault(
				Session.SyncReportsOptionId, LobbyInfo.GlobalSettings.EnableSyncReports);

			generateSyncReport = Connection is not ReplayConnection
				&& LobbyInfo.GlobalSettings.EnableSyncReports
				&& reportsWanted
				&& (humanClients > 1 || (TestMode.IsActive && TestMode.ForceSyncReports));

			// Stated out loud so a missing report is diagnosable BEFORE the next desync rather
			// than after it. If a game ever ends in "No sync report available", this line says
			// whether reporting was ever armed, and which gate said no.
			Log.Write("debug", $"Sync reports {(generateSyncReport ? "enabled" : "disabled")} " +
				$"(setting {LobbyInfo.GlobalSettings.EnableSyncReports}, lobby option {reportsWanted}, " +
				$"human clients {humanClients}, replay {Connection is ReplayConnection}).");

			OpenSyncHashLog();

			NetFrameNumber = 1;
			LocalFrameNumber = 0;
			LastTickTime.Value = Game.RunTime;

			Connection.StartGame();
		}

		public OrderManager(IConnection conn)
		{
			Connection = conn;
			syncReport = new SyncReport(this);

			LastTickTime = new TickTime(() => SuggestedTimestep, Game.RunTime);
		}

		public void IssueOrders(Order[] orders)
		{
			foreach (var order in orders)
				IssueOrder(order);
		}

		public void IssueOrder(Order order)
		{
			if (order.IsImmediate)
				localImmediateOrders.Add(order);
			else
				localOrders.Add(order);
		}

		void SendImmediateOrders()
		{
			if (localImmediateOrders.Count != 0 && GameSaveLastFrame < NetFrameNumber)
				Connection.SendImmediate(localImmediateOrders);
			localImmediateOrders.Clear();
		}

		public void ReceiveDisconnect(int clientId, int frame)
		{
			// All clients must process the disconnect on the same world tick to allow synced actions to run deterministically.
			// The server guarantees that we will not receive any more order packets from this client from this frame, so we
			// can insert a marker in the orders stream and process the synced disconnect behaviours on the first tick of that frame.
			if (GameStarted)
				ReceiveOrders(clientId, (frame, ClientDisconnected));

			// The Client state field is not synced; update it immediately so it can be shown in the UI
			var client = LobbyInfo.ClientWithIndex(clientId);
			if (client != null)
				client.State = Session.ClientState.Disconnected;
		}

		public void ReceiveSync((int Frame, int SyncHash, ulong DefeatState) sync)
		{
			if (syncForFrame.TryGetValue(sync.Frame, out var s))
			{
				if (s.SyncHash != sync.SyncHash || s.DefeatState != sync.DefeatState)
					OutOfSync(sync.Frame);
			}
			else
				syncForFrame.Add(sync.Frame, (sync.SyncHash, sync.DefeatState));
		}

		public void ReceiveTickScale(float scale)
		{
			tickScale = scale;
		}

		public void ReceiveImmediateOrders(int clientId, OrderPacket orders)
		{
			foreach (var o in orders.GetOrders(World))
			{
				UnitOrders.ProcessOrder(this, World, clientId, o);

				// A mod switch or other event has pulled the ground from beneath us
				if (disposed)
					return;
			}
		}

		public void ReceiveOrders(int clientId, (int Frame, OrderPacket Orders) orders)
		{
			if (pendingOrders.TryGetValue(clientId, out var queue))
				queue.Enqueue((orders.Frame, orders.Orders));
			else
				throw new InvalidDataException($"Received packet from disconnected client '{clientId}'");
		}

		void ReceiveAllOrdersAndCheckSync()
		{
			Connection.Receive(this);
		}

		bool IsReadyForNextFrame => GameStarted && pendingOrders.All(p => p.Value.Count > 0);

		public int SuggestedTimestep
		{
			get
			{
				if (World == null)
					return Ui.Timestep;

				if (World.IsLoadingGameSave)
					return 1;

				if (World.IsReplay)
					return World.ReplayTimestep;

				if (tickScale != 1f)
					return Math.Max((int)(tickScale * World.Timestep), 1);

				return World.Timestep;
			}
		}

		void SendOrders()
		{
			if (GameStarted && GameSaveLastFrame < NetFrameNumber && sentOrdersFrame < NetFrameNumber)
			{
				Connection.Send(NetFrameNumber, localOrders);
				localOrders.Clear();
				sentOrdersFrame = NetFrameNumber;
			}
		}

		void ProcessOrders()
		{
			foreach (var (clientId, frameOrders) in pendingOrders)
			{
				// The IsReadyForNextFrame check above guarantees that all clients have sent a packet
				var (frameNumber, orders) = frameOrders.Dequeue();

				// We expect every frame to have a queued order packet, even if it contains no orders, as this
				// controls the pacing of the game simulation.
				// Sanity check that we are processing the frame that we expect, so we can crash early instead of desyncing.
				if (frameNumber != NetFrameNumber)
					throw new InvalidDataException($"Attempted to process orders from client {clientId} for frame {frameNumber} on frame {NetFrameNumber}");

				if (orders == ClientDisconnected)
				{
					processClientsToRemove.Add(clientId);
					World.OnClientDisconnected(clientId);

					continue;
				}

				foreach (var order in orders.GetOrders(World))
				{
					UnitOrders.ProcessOrder(this, World, clientId, order);
					processClientOrders.Add(new ClientOrder { Client = clientId, Order = order });
				}
			}

			foreach (var clientId in processClientsToRemove)
				pendingOrders.Remove(clientId);

			var syncHash = 0;
			var defeatState = 0UL;
			if (NetFrameNumber >= GameSaveLastSyncFrame)
			{
				for (var i = 0; i < World.Players.Length; i++)
					if (World.Players[i].WinState == WinState.Lost)
						defeatState |= 1UL << i;

				syncHash = World.SyncHash();
			}

			Connection.SendSync(NetFrameNumber, syncHash, defeatState);

			// The trace records the hash that was actually sent, including the pre-GameSave
			// zeroes, so a reader never has to reconstruct which branch a frame took.
			syncHashLog?.WriteLine(
				$"{NetFrameNumber}\t{syncHash}\t{World.SharedRandom.Last}\t{World.SharedRandom.TotalCount}\t{defeatState}");

			if (generateSyncReport)
				using (new PerfSample("sync_report"))
					syncReport.UpdateSyncReport(processClientOrders);

			processClientOrders.Clear();
			processClientsToRemove.Clear();

			++NetFrameNumber;
		}

		// DIAGNOSTIC ONLY (Test.ForceSyncReports). Called when the server acknowledges a game save,
		// to capture the RECORDING side of the sync state for the frames the restore will later
		// validate against. Dumps a window rather than a single frame because the client's frame at
		// acknowledgement is only approximately the save's LastSyncFrame, and the restore fails on
		// whichever frame the save recorded. The ring holds 32, so a window of 12 is safely inside it.
		internal void DumpRecordingSideSyncReports()
		{
			if (!generateSyncReport)
			{
				Log.Write("debug", "[syncdiag] recording-side dump skipped: sync reports are not armed.");
				return;
			}

			var last = NetFrameNumber;
			var first = Math.Max(1, last - 12);
			for (var f = first; f <= last; f++)
				syncReport.DumpDiagnosticReport(f, "recorded");

			Log.Write("debug", $"[syncdiag] recording-side sync reports dumped for frames {first}-{last}.");
		}

		// DIAGNOSTIC ONLY (Test.SyncHashLog). The header names the runtime that produced the
		// trace, so a cross-runtime comparison cannot be fooled by a launcher that silently
		// selected the wrong one — the evidence and the proof of provenance are the same file.
		void OpenSyncHashLog()
		{
			var path = TestMode.IsActive ? TestMode.SyncHashLogPath : null;
			if (string.IsNullOrEmpty(path))
				return;

			try
			{
				var dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir))
					Directory.CreateDirectory(dir);

				// AutoFlush on: a match killed by a wall-clock watchdog must still leave every
				// frame it reached on disk, or the trace silently ends before the interesting one.
				syncHashLog = new StreamWriter(path, append: false) { AutoFlush = true };
				syncHashLog.WriteLine($"# runtime\t{Platform.RuntimeVersion}");
				syncHashLog.WriteLine($"# platform\t{Platform.CurrentPlatform}\t{Environment.OSVersion}");
				syncHashLog.WriteLine($"# seed\t{TestMode.ResolvedSeed}");
				syncHashLog.WriteLine($"# build\t{BuildFingerprint.ForMod(Game.ModData)}");
				syncHashLog.WriteLine("# netframe\tsynchash\tsharedrandom\trandomdraws\tdefeatstate");
			}
			catch (Exception e)
			{
				syncHashLog = null;
				Log.Write("debug", $"[synchash] could not open {path}: {e.Message}");
			}
		}

		public void Dispose()
		{
			disposed = true;
			syncHashLog?.Dispose();
			syncHashLog = null;
			Connection?.Dispose();
		}

		public void TickImmediate()
		{
			SendImmediateOrders();

			ReceiveAllOrdersAndCheckSync();
		}

		public bool TryTick()
		{
			var shouldTick = true;

			if (IsNetFrame)
			{
				// Check whether or not we will be ready for a tick next frame
				// We don't need to include ourselves in the equation because we can always generate orders this frame
				shouldTick = pendingOrders.All(p => p.Key == Connection.LocalClientId || p.Value.Count > 0);

				// Send orders only if we are currently ready, this prevents us sending orders too soon if we are
				// stalling
				if (shouldTick)
					SendOrders();
			}

			var willTick = shouldTick;
			if (willTick && IsNetFrame)
			{
				willTick = IsReadyForNextFrame;
				if (willTick)
					ProcessOrders();
			}

			if (willTick)
				LocalFrameNumber++;

			return willTick;
		}

		// The server may request clients to batch multiple frames worth of orders into a single packet
		// to improve robustness against network jitter at the expense of input latency
		bool IsNetFrame => LocalFrameNumber % LobbyInfo.GlobalSettings.NetFrameInterval == 0;
	}
}
