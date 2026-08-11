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
using NUnit.Framework;
using OpenRA.Network;

namespace OpenRA.Test
{
	[TestFixture]
	public class NetworkDiagnosticsTest
	{
		[TestCase("10.0.0.1", true)]
		[TestCase("10.255.255.254", true)]
		[TestCase("192.168.0.35", true)]
		[TestCase("172.16.0.1", true)]
		[TestCase("172.31.255.254", true)]
		[TestCase("172.15.0.1", false)]
		[TestCase("172.32.0.1", false)]
		[TestCase("100.64.0.1", false)]
		[TestCase("8.8.8.8", false)]
		[TestCase("169.254.1.1", false)]
		public void PrivateAddressesAreRecognised(string address, bool expected)
		{
			Assert.That(NetworkDiagnostics.IsPrivate(IPAddress.Parse(address)), Is.EqualTo(expected));
		}

		// 100.64.0.0/10 stops at 100.127.255.255 — the boundary a /8 or /16 reading gets wrong, and
		// the whole point of the check is telling a fixable router problem from an unfixable ISP one.
		[TestCase("100.64.0.0", true)]
		[TestCase("100.100.1.1", true)]
		[TestCase("100.127.255.255", true)]
		[TestCase("100.63.255.255", false)]
		[TestCase("100.128.0.0", false)]
		[TestCase("192.168.0.35", false)]
		public void CarrierGradeNatRangeIsRecognised(string address, bool expected)
		{
			Assert.That(NetworkDiagnostics.IsCarrierGradeNat(IPAddress.Parse(address)), Is.EqualTo(expected));
		}

		[Test]
		public void AddressClassifiersRejectNullAndIPv6()
		{
			Assert.That(NetworkDiagnostics.IsPrivate(null), Is.False);
			Assert.That(NetworkDiagnostics.IsCarrierGradeNat(null), Is.False);
			Assert.That(NetworkDiagnostics.IsPrivate(IPAddress.IPv6Loopback), Is.False);
			Assert.That(NetworkDiagnostics.IsCarrierGradeNat(IPAddress.IPv6Loopback), Is.False);
		}

		[Test]
		public void LocalAddressIsPrivateOrNothing()
		{
			// The probe answers with whichever interface wins the route lookup, which a VPN or container
			// bridge can be — Tailscale's exit-node routes outrank the default route entirely. Null is a
			// legitimate answer; a tunnel address, a public address or the wildcard never is, because
			// the value is handed to a player as the thing to aim a router rule at. Asserting the
			// contract rather than a specific address keeps this meaningful on any machine.
			var address = NetworkDiagnostics.GetLocalAddress();
			TestContext.WriteLine($"GetLocalAddress() = {address?.ToString() ?? "null"}");

			if (address == null)
				return;

			Assert.That(address.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
			Assert.That(NetworkDiagnostics.IsPrivate(address), Is.True,
				$"{address} is not RFC 1918, so no LAN port-forward rule could target it.");
			Assert.That(address, Is.Not.EqualTo(IPAddress.Any));
			Assert.That(IPAddress.IsLoopback(address), Is.False);

			var b = address.GetAddressBytes();
			Assert.That(b[0] == 169 && b[1] == 254, Is.False, "a link-local VPN adapter address leaked through");
			Assert.That(NetworkDiagnostics.IsCarrierGradeNat(address), Is.False);
		}
	}
}
