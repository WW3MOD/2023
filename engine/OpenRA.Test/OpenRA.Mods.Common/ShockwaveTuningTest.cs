#region Copyright & License Information
/*
 * WW3MOD shockwave rings — configurability without drift.
 *
 * Two separate claims are pinned here, because both are the kind that a reviewer can only take on
 * trust otherwise.
 *
 * 1. THE NEW FIELDS DEFAULT TO THE OLD HARDCODED CONSTANTS, BIT FOR BIT. The ring's band geometry
 *    and thickness ramp used to be float literals in ShockwaveEffect and ExpandingShockwaveRenderable
 *    (0.75f, 0.55f, 0.15f, 2.5f). They are now `percent / 100f`. That rewriting is only safe because
 *    IEEE division of two exactly-representable operands is CORRECTLY ROUNDED, so 55/100f lands on
 *    the same float the literal 0.55f denotes rather than merely near it. If that were untrue every
 *    existing shockwave in the game would shift by a pixel or two with no YAML change and nobody
 *    would notice for months. Asserted on the bit patterns, not with a tolerance.
 *
 * 2. THE 60% RING CUT ON THE VOLATILE-CARGO BANDS DID NOT MOVE ANY BAND'S LETHALITY. The user asked
 *    for a smaller ring on the loaded supply truck. MaxRadius binds two things at once — how far the
 *    wave TRAVELS and, jointly with Spread x Falloff, how far it HURTS — so editing it would have
 *    quietly nerfed the four bands where it is the smaller term. The fix was a separate visual field.
 *    This reads the shipped YAML rather than a copy of it, so a later edit that shrinks MaxRadius
 *    "to make the ring smaller" fails here instead of silently costing those bands their damage.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ShockwaveTuningTest
	{
		// The literals that were compiled into the ring before these became fields.
		const float LegacyPeakOuter = 0.75f;
		const float LegacyPeakInner = 0.55f;
		const float LegacyOuterFeather = 0.15f;
		const float LegacyThicknessRamp = 2.5f;
		const int LegacySegments = 64;

		static void AssertSameFloat(float expected, float actual, string what)
		{
			Assert.That(BitConverter.SingleToInt32Bits(actual), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)),
				$"{what}: {actual} is not bit-identical to the constant {expected} it replaced. " +
				"Every shipped shockwave would render differently with no YAML change.");
		}

		[Test]
		public void PercentFieldsReproduceTheHardcodedFloatsExactly()
		{
			var w = new ShockwaveDamageWarhead();

			AssertSameFloat(LegacyPeakOuter, w.ShockwavePeakOuterPercent / 100f, "ShockwavePeakOuterPercent");
			AssertSameFloat(LegacyPeakInner, w.ShockwavePeakInnerPercent / 100f, "ShockwavePeakInnerPercent");
			AssertSameFloat(LegacyOuterFeather, w.ShockwaveOuterFeatherPercent / 100f, "ShockwaveOuterFeatherPercent");
			AssertSameFloat(LegacyThicknessRamp, w.ShockwaveThicknessRampPercent / 100f, "ShockwaveThicknessRampPercent");
			Assert.That(w.ShockwaveSegments, Is.EqualTo(LegacySegments));
		}

		/// <summary>
		/// The scalars being identical is the whole proof — multiplication is deterministic given
		/// identical operands — but the products are the thing that actually reaches the screen, so
		/// they are swept across the band widths and progress values the mod really produces.
		/// </summary>
		[Test]
		public void RingGeometryIsUnchangedAcrossTheRangeTheModActuallyUses()
		{
			var w = new ShockwaveDamageWarhead();

			// Band widths from a 192-unit ring (VolatileLoad1) up past the 1536 of the tactical nuke.
			for (var bandWidth = 0; bandWidth <= 2048; bandWidth++)
			{
				Assert.That((int)(bandWidth * (w.ShockwavePeakOuterPercent / 100f)),
					Is.EqualTo((int)(bandWidth * LegacyPeakOuter)), $"peak outer radius at bandWidth {bandWidth}");
				Assert.That((int)(bandWidth * (w.ShockwavePeakInnerPercent / 100f)),
					Is.EqualTo((int)(bandWidth * LegacyPeakInner)), $"peak inner radius at bandWidth {bandWidth}");
				Assert.That((int)(bandWidth * (w.ShockwaveOuterFeatherPercent / 100f)),
					Is.EqualTo((int)(bandWidth * LegacyOuterFeather)), $"outer feather at bandWidth {bandWidth}");
			}

			// Thickness ramp over the full 0->1 expansion progress, at finer steps than any ring ticks.
			for (var i = 0; i <= 10000; i++)
			{
				var progress = i / 10000f;
				Assert.That(Math.Min(1f, progress * (w.ShockwaveThicknessRampPercent / 100f)),
					Is.EqualTo(Math.Min(1f, progress * LegacyThicknessRamp)), $"thickness ramp at progress {progress}");
			}
		}

		[Test]
		public void DefaultRingShapeMatchesTheStructDefault()
		{
			var w = new ShockwaveDamageWarhead();
			var shape = w.RingShape;

			Assert.That(shape.Segments, Is.EqualTo(ShockwaveRingShape.Default.Segments));
			Assert.That(shape.PeakOuterPercent, Is.EqualTo(ShockwaveRingShape.Default.PeakOuterPercent));
			Assert.That(shape.PeakInnerPercent, Is.EqualTo(ShockwaveRingShape.Default.PeakInnerPercent));
			Assert.That(shape.OuterFeatherPercent, Is.EqualTo(ShockwaveRingShape.Default.OuterFeatherPercent));
		}

		/// <summary>
		/// An unset ShockwaveVisualRadius has to mean "exactly MaxRadius", not "close to it": the
		/// render path divides by it to get expansion progress, so any other fallback would change
		/// the alpha and thickness curve of every shockwave already in the game.
		/// </summary>
		[Test]
		public void UnsetVisualRadiusFollowsMaxRadius()
		{
			var w = new ShockwaveDamageWarhead();

			Assert.That(w.ShockwaveVisualRadius, Is.EqualTo(WDist.Zero), "the opt-in sentinel must stay zero");
			Assert.That(w.VisualRadius, Is.EqualTo(w.MaxRadius));
		}

		// ---- The fade-out curve ----

		static ShockwaveDamageWarhead Warhead(params (string Field, string Value)[] fields)
		{
			var w = new ShockwaveDamageWarhead();
			foreach (var (field, value) in fields)
				FieldLoader.LoadField(w, field, value);

			return w;
		}

		/// <summary>
		/// Whatever the curve does in between, it has to start at full alpha and land exactly on
		/// ShockwaveEndAlphaPercent. Missing the far endpoint is the failure the user actually reported:
		/// a ring still drawn at a fifth of full brightness on the last frame before it is removed.
		/// </summary>
		[Test]
		public void EveryExponentStartsAtFullAlphaAndLandsOnTheDeclaredEndAlpha()
		{
			foreach (var endAlpha in new[] { 0, 1, 35, 50, 100 })
			{
				foreach (var exponent in new[] { 50, 100, 200, 300, 1000 })
				{
					var w = Warhead(("ShockwaveEndAlphaPercent", endAlpha.ToString()),
						("ShockwaveFadeOutExponentPercent", exponent.ToString()));

					Assert.That(w.FadeOutAt(0f), Is.EqualTo(1f).Within(1e-6f),
						$"end {endAlpha} exponent {exponent}: the ring does not start at full alpha");
					Assert.That(w.FadeOutAt(1f), Is.EqualTo(endAlpha / 100f).Within(1e-6f),
						$"end {endAlpha} exponent {exponent}: the ring does not reach its declared end alpha");
				}
			}
		}

		/// <summary>
		/// The default ring fades to NOTHING, and does its fading late. This is the whole of the user's
		/// request of 2026-08-30 — rings that "become fully faded out" at their largest rather than
		/// terminating while solid — so both halves are pinned: the endpoint, which is one integer away
		/// from being lost, and the shape, which is what distinguishes the fix from the straight line
		/// that was already reaching zero and was still being reported as chunky.
		/// </summary>
		[Test]
		public void TheDefaultRingHoldsItsAlphaEarlyAndSpendsItAtTheEdge()
		{
			var w = new ShockwaveDamageWarhead();

			Assert.That(w.ShockwaveEndAlphaPercent, Is.EqualTo(0),
				"a ring that stops above zero alpha is cut off rather than faded out");
			Assert.That(w.FadeOutAt(1f), Is.Zero, "the ring is still being drawn at its widest");

			// Through the first third of travel the ring is barely dimmed — that stretch is where a small
			// ring is still emerging from its own fireball sprite. The linear ramp had shed 30% by here.
			Assert.That(w.FadeOutAt(0.3f), Is.GreaterThan(0.9f),
				"the ring has started fading while it is still hidden inside the explosion graphic");

			// And by the last twentieth it is all but gone, so nothing solid is on screen to be cut off.
			Assert.That(w.FadeOutAt(0.95f), Is.LessThan(0.1f),
				"the ring is still carrying a tenth of its alpha into the final frames");
		}

		/// <summary>
		/// Exponent 100 is the documented escape hatch back to the straight-line ramp this replaced,
		/// so it has to actually BE that ramp rather than merely resemble it.
		/// </summary>
		[Test]
		public void ExponentOneHundredIsTheLinearRampItReplaced()
		{
			foreach (var endAlpha in new[] { 0, 35, 100 })
			{
				var w = Warhead(("ShockwaveEndAlphaPercent", endAlpha.ToString()),
					("ShockwaveFadeOutExponentPercent", "100"));

				for (var i = 0; i <= 1000; i++)
				{
					var progress = i / 1000f;
					var linear = 1f - (progress * (1f - (endAlpha / 100f)));
					Assert.That(w.FadeOutAt(progress), Is.EqualTo(linear).Within(1e-6f),
						$"end {endAlpha} at progress {progress}");
				}
			}
		}

		/// <summary>
		/// THE DEFAULT CURVE IS NOWHERE DIMMER THAN THE LINE IT REPLACED, and that is a safety claim,
		/// not a cosmetic one. A shockwave ring competes with its own explosion sprite for the first
		/// stretch of its travel, and the TOS ring was already shipped INVISIBLE once by tuning that
		/// stretch down (see weapons-ballistics.yaml). Loading the fade into the end is only a free
		/// change because it cannot darken any frame; if someone later drops the default below 100 this
		/// fails, which is the point.
		/// </summary>
		[Test]
		public void TheDefaultCurveNeverDarkensAnyFrameAgainstTheOldLinearRamp()
		{
			var w = new ShockwaveDamageWarhead();

			for (var i = 0; i <= 1000; i++)
			{
				var progress = i / 1000f;
				Assert.That(w.FadeOutAt(progress), Is.GreaterThanOrEqualTo((1f - progress) - 1e-6f),
					$"the default fade is dimmer than the linear ramp at progress {progress}, " +
					"which is how a ring gets tuned back into its own fireball");
			}
		}

		[Test]
		public void ANonPositiveFadeExponentIsRefusedAtLoad()
		{
			foreach (var bad in new[] { "0", "-100" })
			{
				var w = Warhead(("ShockwaveFadeOutExponentPercent", bad));
				Assert.That(() => ((IRulesetLoaded<WeaponInfo>)w).RulesetLoaded(null, null),
					Throws.TypeOf<YamlException>(),
					$"ShockwaveFadeOutExponentPercent: {bad} flattens the fade to a constant instead of failing");
			}
		}

		/// <summary>
		/// The decorative short-circuit skips the per-tick actor sweep. It must not catch a warhead
		/// that damages by any route — Damage is only one of three additive terms InflictDamage reads.
		/// </summary>
		[Test]
		public void DeliversDamageCoversEveryAdditiveTerm()
		{
			Assert.That(new ShockwaveDamageWarhead().DeliversDamage, Is.False,
				"a warhead with no damage fields set is decorative");

			foreach (var field in new[] { "Damage", "DamagePercent", "RandomDamageAddition" })
			{
				var w = new ShockwaveDamageWarhead();
				FieldLoader.LoadField(w, field, "7");
				Assert.That(w.DeliversDamage, Is.True,
					$"{field} makes the wave hurt, so the actor sweep must not be skipped");
			}
		}

		// ---- The volatile-cargo bands, read from the shipped YAML ----

		// Pinned per band: MaxRadius, and the damage reach that MaxRadius plus Spread x Falloff
		// produced BEFORE the ring was cut to 60%. Bands 1-2 are bounded by MaxRadius, band 3 onward
		// by the falloff table's last step at 4 * 341 = 1364.
		static readonly (string Weapon, int MaxRadius, int DamageReach)[] Bands =
		{
			("VolatileLoad1", 512, 512),
			("VolatileLoad2", 1024, 1024),
			("VolatileLoad3", 1536, 1364),
			("VolatileLoad4", 2048, 1364),
			("VolatileLoad5", 2560, 1364),
			("VolatileLoad6", 3072, 1364),
			("VolatileLoad7", 3584, 1364),
			("VolatileLoad8", 4096, 1364),
		};

		static string FindRules(params string[] parts)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(parts).ToArray());
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/rules/{string.Join("/", parts)}");
		}

		static Dictionary<string, MiniYaml> ShockwaveWarheads(string file)
		{
			var found = new Dictionary<string, MiniYaml>();
			foreach (var node in MiniYaml.FromFile(FindRules("weapons", file)))
			{
				var shockwave = node.Value.Nodes.FirstOrDefault(n => n.Key == "Warhead@Shockwave");
				if (shockwave != null)
					found[node.Key] = shockwave.Value;
			}

			return found;
		}

		static int Dist(MiniYaml warhead, string key, string weapon)
		{
			var raw = warhead.Nodes.FirstOrDefault(n => n.Key == key)?.Value.Value;
			Assert.That(raw, Is.Not.Null, $"{weapon} has no {key} — this test is scanning nothing");
			Assert.That(WDist.TryParse(raw, out var d), Is.True, $"{weapon}'s {key} ('{raw}') is not a WDist");
			return d.Length;
		}

		[Test]
		public void VolatileBandRingsWereCutToSixtyPercentWithoutTouchingLethality()
		{
			var warheads = ShockwaveWarheads("weapons-explosions.yaml");

			foreach (var (weapon, expectedMax, expectedReach) in Bands)
			{
				Assert.That(warheads.ContainsKey(weapon), Is.True, $"{weapon} has no Warhead@Shockwave");
				var w = warheads[weapon];

				var maxRadius = Dist(w, "MaxRadius", weapon);
				var spread = Dist(w, "Spread", weapon);
				var visual = Dist(w, "ShockwaveVisualRadius", weapon);

				var falloff = w.Nodes.FirstOrDefault(n => n.Key == "Falloff")?.Value.Value;
				Assert.That(falloff, Is.Not.Null, $"{weapon} has no Falloff");
				var steps = falloff.Split(',').Length;

				// The claim the user was given: the ring shrank, the damage did not.
				var reach = Math.Min(maxRadius, (steps - 1) * spread);
				Assert.That(reach, Is.EqualTo(expectedReach),
					$"{weapon}'s damage reach moved from {expectedReach} to {reach}. The 60% cut was supposed to be " +
					"visual only. If MaxRadius was lowered to shrink the ring, undo that and lower " +
					"ShockwaveVisualRadius instead — see the comment block above ^VolatileLoadEffects.");

				Assert.That(maxRadius, Is.EqualTo(expectedMax), $"{weapon}'s MaxRadius changed");

				// 60% of MaxRadius, allowing the rounding of an odd product to the nearest unit.
				Assert.That(visual, Is.EqualTo(expectedMax * 3 / 5).Within(1),
					$"{weapon}'s ring is no longer 60% of its wave travel");
				Assert.That(visual, Is.LessThan(maxRadius), $"{weapon}'s ring is not actually smaller than the wave");
			}
		}

		/// <summary>
		/// The TOS fires 24 rockets 10 ticks apart. Its ring is decorative on purpose — giving it any
		/// damage would be a balance change on the mod's heaviest anti-infantry weapon — and it is
		/// sized so that one ring has died before the next rocket lands.
		/// </summary>
		[Test]
		public void TosRingIsDecorativeAndDiesBeforeTheNextRocketLands()
		{
			var w = ShockwaveWarheads("weapons-ballistics.yaml")["TosRockets"];

			foreach (var damaging in new[] { "Damage", "DamagePercent", "RandomDamageAddition" })
			{
				var declared = w.Nodes.FirstOrDefault(n => n.Key == damaging)?.Value.Value;
				Assert.That(declared ?? "0", Is.EqualTo("0"),
					$"the TOS ring declares {damaging}: {declared}. It is meant to be pure spectacle; " +
					"giving it damage rebalances the TOS.");
			}

			// The user's report of 2026-08-30 was about THIS ring specifically: it was the only one in
			// the mod holding alpha at full radius, and it read as ending rather than fading. Both the
			// override and the floor it stood on are gone; either coming back reintroduces the report.
			var endAlpha = w.Nodes.FirstOrDefault(n => n.Key == "ShockwaveEndAlphaPercent")?.Value.Value;
			Assert.That(endAlpha ?? "0", Is.EqualTo("0"),
				$"the TOS ring declares ShockwaveEndAlphaPercent: {endAlpha}, so it is drawn at " +
				$"{endAlpha}% of full alpha on the last frame before it vanishes. That is the " +
				"\"too chunky, should fade out\" report; see the comment on the warhead.");

			var maxRadius = Dist(w, "MaxRadius", "TosRockets");
			var waveSpeed = int.Parse(w.Nodes.First(n => n.Key == "WaveSpeed").Value.Value);

			// ShockwaveEffect expands by 1024 / WaveSpeed each tick and ends past MaxRadius.
			var lifetime = maxRadius / (1024 / waveSpeed);
			Assert.That(lifetime, Is.LessThanOrEqualTo(10),
				$"a TOS ring lives {lifetime} ticks against BurstDelays of 10, so a 24-rocket salvo would " +
				"stack overlapping rings. Lower MaxRadius or WaveSpeed.");
		}
	}
}
