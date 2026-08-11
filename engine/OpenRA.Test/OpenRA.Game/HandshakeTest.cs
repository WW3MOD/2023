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

using NUnit.Framework;
using OpenRA.Network;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class HandshakeTest
	{
		[TestCase(TestName = "Handshake round-trip preserves the build fingerprint")]
		public void RoundTripPreservesBuildFingerprint()
		{
			var response = new HandshakeResponse
			{
				Mod = "ww3mod",
				Version = "release-20230225",
				OrdersProtocol = 7,
				BuildFingerprint = "b0fa20d41c+1a2b3c4d/3f9a2c71",
				Client = new Session.Client()
			};

			var parsed = HandshakeResponse.Deserialize(response.Serialize(), "test");

			Assert.That(parsed.BuildFingerprint, Is.EqualTo("b0fa20d41c+1a2b3c4d/3f9a2c71"));
		}

		[TestCase(TestName = "A handshake from a client that predates the build check parses to a null fingerprint")]
		public void MissingBuildFingerprintParsesAsNull()
		{
			// Exactly what a pre-check client puts on the wire. The server must be able to read
			// it and reject it deliberately, rather than throwing on the unknown shape - a crash
			// in ValidateClient would take the host's game down instead of turning one player away.
			const string OldClientPayload =
				"Handshake:\n" +
				"\tMod: ww3mod\n" +
				"\tVersion: release-20230225\n" +
				"\tOrdersProtocol: 7\n" +
				"Client:\n" +
				"\tName: friend\n";

			var parsed = HandshakeResponse.Deserialize(OldClientPayload, "test");

			Assert.That(parsed.Mod, Is.EqualTo("ww3mod"));
			Assert.That(parsed.BuildFingerprint, Is.Null);
		}

		[TestCase(TestName = "A handshake carrying fields this build does not know is ignored, not rejected")]
		public void UnknownFieldsAreIgnored()
		{
			// The mirror case: an OLD server reading a NEW client's handshake. FieldLoader walks
			// the type's fields and looks each one up in the yaml, so nodes it has never heard of
			// are skipped. This is what lets one player upgrade before the other without the
			// upgraded side becoming unable to connect at all.
			const string NewerClientPayload =
				"Handshake:\n" +
				"\tMod: ww3mod\n" +
				"\tVersion: release-20230225\n" +
				"\tOrdersProtocol: 7\n" +
				"\tBuildFingerprint: b0fa20d41c/3f9a2c71\n" +
				"\tSomeFutureField: 12345\n" +
				"Client:\n" +
				"\tName: friend\n";

			var parsed = HandshakeResponse.Deserialize(NewerClientPayload, "test");

			Assert.That(parsed.BuildFingerprint, Is.EqualTo("b0fa20d41c/3f9a2c71"));
		}
	}
}
