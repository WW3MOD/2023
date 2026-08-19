#region Copyright & License Information
/*
 * WW3MOD replay-metadata stamp tests.
 *
 * The build fingerprint is only worth checking if it survives the round trip into a replay file and
 * back, and — just as important — if a replay recorded before the field existed still parses. If
 * adding the field made old replay metadata throw instead of load, the refusal a player saw would
 * be "metadata could not be read", which points at a corrupt file and sends them looking in the
 * wrong place entirely.
 */
#endregion

using System.IO;
using NUnit.Framework;
using OpenRA.FileFormats;

namespace OpenRA.Test
{
	[TestFixture]
	public class GameInformationStampTest
	{
		const string Fingerprint = "b0fa20d41c+1a2b3c4d/3f9a2c71/8d1e04ba";

		[Test]
		public void BuildFingerprintSurvivesTheRoundTrip()
		{
			var info = new GameInformation
			{
				Mod = "ww3mod",
				Version = "release-20230225",
				BuildFingerprint = Fingerprint,
				MapUid = "abc123",
				MapTitle = "Test Map"
			};

			var parsed = GameInformation.Deserialize(info.Serialize(), "test");

			Assert.That(parsed.BuildFingerprint, Is.EqualTo(Fingerprint));
			Assert.That(parsed.Version, Is.EqualTo("release-20230225"));
			Assert.That(parsed.Mod, Is.EqualTo("ww3mod"));
		}

		// Every replay recorded before this change. It must still load and report no stamp, rather
		// than failing to parse.
		[Test]
		public void MetadataWithoutTheStampParsesAsNull()
		{
			const string Recorded =
				"Root:\n" +
				"\tMod: ww3mod\n" +
				"\tVersion: release-20230225\n" +
				"\tMapUid: abc123\n" +
				"\tMapTitle: Test Map\n";

			var parsed = GameInformation.Deserialize(Recorded, "test");

			Assert.That(parsed.BuildFingerprint, Is.Null);
			Assert.That(parsed.MapUid, Is.EqualTo("abc123"),
				"the rest of the metadata must still load, or the refusal would blame a corrupt file");
		}

		// The two tests above stop at Serialize/Deserialize, which is a string round trip and not a
		// FILE one. What ReplayBrowserLogic and BlankLoadScreen actually hand to the compatibility
		// check is whatever ReplayMetadata.Read returns, and Read does not parse from the top: it
		// seeks from the END of the file, reads a length and an end marker, then seeks BACKWARDS by
		// that length to find the block. So the stamp lengthens a byte count that Read navigates by,
		// and a string test cannot see a mistake in it. This writes the real block with the real
		// writer and reads it back with the real reader.
		[Test]
		public void BuildFingerprintSurvivesTheReplayFileRoundTrip()
		{
			var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".orarep");
			try
			{
				var info = new GameInformation
				{
					Mod = "ww3mod",
					Version = "release-20230225",
					BuildFingerprint = Fingerprint,
					MapUid = "abc123",
					MapTitle = "Test Map"
				};

				using (var writer = new BinaryWriter(File.Create(path)))
					new ReplayMetadata(info).Write(writer);

				var read = ReplayMetadata.Read(path);

				Assert.That(read, Is.Not.Null, "the metadata block written by Write was not located by Read");
				Assert.That(read.GameInfo.BuildFingerprint, Is.EqualTo(Fingerprint));
				Assert.That(read.GameInfo.MapUid, Is.EqualTo("abc123"));
			}
			finally
			{
				File.Delete(path);
			}
		}

		// The pre-stamp case through the same path. Read must return metadata reporting no stamp -
		// NOT null. Null is the shape of an unreadable file, and ReplayUtils turns that into the
		// generic "incompatible replay" refusal instead of the advisory it should be showing.
		[Test]
		public void MetadataWithoutTheStampSurvivesTheReplayFileRoundTrip()
		{
			var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".orarep");
			try
			{
				var info = new GameInformation
				{
					Mod = "ww3mod",
					Version = "release-20230225",
					MapUid = "abc123",
					MapTitle = "Test Map"
				};

				using (var writer = new BinaryWriter(File.Create(path)))
					new ReplayMetadata(info).Write(writer);

				var read = ReplayMetadata.Read(path);

				Assert.That(read, Is.Not.Null, "an unstamped replay must read as metadata, not as a corrupt file");
				Assert.That(read.GameInfo.BuildFingerprint, Is.Null.Or.Empty);
				Assert.That(read.GameInfo.Mod, Is.EqualTo("ww3mod"));
			}
			finally
			{
				File.Delete(path);
			}
		}
	}
}
