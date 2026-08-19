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

using NUnit.Framework;

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
	}
}
