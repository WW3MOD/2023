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

using System.Net;
using System.Net.Sockets;

namespace OpenRA.Network
{
	/// <summary>
	/// Facts about how this host is addressed, used to explain why a hosted server is
	/// unreachable rather than only reporting that it is.
	/// </summary>
	public static class NetworkDiagnostics
	{
		// RFC 5737 TEST-NET-1. Never routed anywhere, so naming it cannot be mistaken for
		// contacting a third party.
		static readonly IPEndPoint RouteProbe = new(IPAddress.Parse("192.0.2.1"), 65530);

		/// <summary>
		/// The LAN address a router's port-forward rule has to point at, or null when it cannot
		/// be determined.
		/// </summary>
		public static IPAddress GetLocalAddress()
		{
			// The listen sockets bind Any/IPv6Any, so their LocalEndPoint reads 0.0.0.0 and names no
			// interface. Connecting a UDP socket transmits nothing — it only asks the OS which
			// interface it would route this destination through — and the resulting LocalEndPoint is
			// then the address this machine presents to the router. That is precisely the value a
			// stale port-forward rule has wrong, and the one fact that identifies the mismatch.
			try
			{
				using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
				socket.Connect(RouteProbe);
				return (socket.LocalEndPoint as IPEndPoint)?.Address;
			}
			catch (SocketException)
			{
				// No route off this machine at all (no default gateway): we genuinely cannot tell.
				return null;
			}
		}

		/// <summary>RFC 1918 private address, i.e. behind a router that could be port-forwarded.</summary>
		public static bool IsPrivate(IPAddress address)
		{
			if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
				return false;

			var b = address.GetAddressBytes();
			return b[0] == 10
				|| (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
				|| (b[0] == 192 && b[1] == 168);
		}

		/// <summary>
		/// RFC 6598 shared address space (100.64.0.0/10). The ISP is NATing us too, so no change to
		/// the local router can ever open a port from the internet.
		/// </summary>
		public static bool IsCarrierGradeNat(IPAddress address)
		{
			if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
				return false;

			var b = address.GetAddressBytes();
			return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
		}
	}
}
