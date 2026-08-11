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
		public void LocalAddressNamesAnInterface()
		{
			// Null is a legitimate answer on a host with no route off the machine, but the wildcard
			// never is: reporting 0.0.0.0 to a player is exactly the uselessness this replaces.
			var address = NetworkDiagnostics.GetLocalAddress();
			TestContext.WriteLine($"GetLocalAddress() = {address?.ToString() ?? "null"}");

			if (address == null)
				Assert.Ignore("No route off this machine, so there is no local address to report.");

			Assert.That(address.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
			Assert.That(address, Is.Not.EqualTo(IPAddress.Any));
		}
	}
}
