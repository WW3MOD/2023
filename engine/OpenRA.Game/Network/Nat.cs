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
using System.Net;
using System.Threading;
using Mono.Nat;

namespace OpenRA.Network
{
	public enum NatStatus { Enabled, Disabled, NotSupported }

	/// <summary>
	/// How the last port forward attempt ended. A bare success/failure cannot tell a host whether
	/// discovery was switched off, whether the router was never found, or whether it was found and
	/// refused the mapping — three different problems with three different fixes.
	/// </summary>
	public enum NatForwardStatus { NotAttempted, DiscoveryDisabled, NoDeviceFound, DeviceRejected, Forwarded }

	public static class Nat
	{
		public static NatStatus Status => NatUtility.IsSearching ? natDevice != null ? NatStatus.Enabled : NatStatus.NotSupported : NatStatus.Disabled;

		public static NatForwardStatus ForwardStatus { get; private set; } = NatForwardStatus.NotAttempted;

		/// <summary>
		/// The WAN address of the discovered router, or null when no device was found — in which case
		/// it is genuinely unknowable from here and must not be guessed at.
		/// </summary>
		public static IPAddress ExternalAddress { get; private set; }

		static Mapping mapping;
		static INatDevice natDevice;
		static bool initialized;

		public static void Initialize()
		{
			if (initialized)
				return;

			if (Game.Settings.Server.DiscoverNatDevices)
			{
				NatUtility.DeviceFound += DeviceFound;
				NatUtility.StartDiscovery();
			}

			initialized = true;
		}

		static readonly SemaphoreSlim Locker = new(1, 1);

		static async void DeviceFound(object sender, DeviceEventArgs args)
		{
			await Locker.WaitAsync();
			try
			{
				// Only interact with one at a time. Some support both UPnP and NAT-PMP.
				natDevice = args.Device;

				Log.Write("nat", $"Device found: {natDevice.DeviceEndpoint}");
				Log.Write("nat", $"Type: {natDevice.NatProtocol}");

				// Needed to tell "the router cannot open this port" apart from "the ISP has put us
				// behind carrier-grade NAT, so no router can ever open it".
				try
				{
					ExternalAddress = await natDevice.GetExternalIPAsync();
					Log.Write("nat", $"External address: {ExternalAddress}");
				}
				catch (Exception e)
				{
					Log.Write("nat", "Failed to query the external address.");
					Log.Write("nat", e);
				}
			}
			finally
			{
				Locker.Release();
			}
		}

		public static NatForwardStatus TryForwardPort(int listen, int external)
		{
			if (natDevice == null)
			{
				// Status distinguishes "never started searching" (the setting is off) from "searching
				// but nothing answered" (the router does not speak UPnP/NAT-PMP, or has it disabled).
				ForwardStatus = Status == NatStatus.Disabled ? NatForwardStatus.DiscoveryDisabled : NatForwardStatus.NoDeviceFound;
				Log.Write("nat", ForwardStatus == NatForwardStatus.DiscoveryDisabled
					? "Not forwarding: UPnP/NAT-PMP discovery is disabled in the settings."
					: "Not forwarding: discovery is running but no UPnP/NAT-PMP device was found.");

				return ForwardStatus;
			}

			var lifetime = Game.Settings.Server.NatPortMappingLifetime;
			mapping = new Mapping(Protocol.Tcp, listen, external, lifetime, "OpenRA");
			try
			{
				natDevice.CreatePortMap(mapping);
			}
			catch (Exception e)
			{
				Log.Write("nat", $"Port forwarding failed: the device refused a TCP {external} -> {listen} mapping.");
				Log.Write("nat", e);
				return ForwardStatus = NatForwardStatus.DeviceRejected;
			}

			Log.Write("nat", $"Forwarded TCP {external} -> {listen} for {lifetime} seconds.");
			return ForwardStatus = NatForwardStatus.Forwarded;
		}

		public static bool TryRemovePortForward()
		{
			if (natDevice == null)
				return false;

			try
			{
				natDevice.DeletePortMap(mapping);
			}
			catch (Exception e)
			{
				Log.Write("nat", "Port removal failed.");
				Log.Write("nat", e);
				return false;
			}

			return true;
		}
	}
}
