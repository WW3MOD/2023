#region Copyright & License Information
/*
 * WW3MOD @experimental — standing-population floors that phase in with the force they support (pure integer).
 *
 * PERCEIVED BEHAVIOUR: the bot opens with line infantry it can fight with, and support units arrive once
 * there is something for them to support — instead of spending its first call-ins on two medics with no
 * squad to attach to and no casualties to treat.
 *
 * THE DEFECT THIS EXISTS TO FIX, stated generally because it is NOT about medics. A flat UnitFloors entry is
 * a standing minimum with NO DENOMINATOR, and UnitBuilderBotModule.ChooseBelowFloor pre-empts the deficit
 * argmax, the target ceiling and every demand gate to satisfy it. At t=0 every census is zero, so EVERY
 * floor is maximally unmet at exactly the moment its need is lowest — and floored support types are cheap,
 * so they clear first and become the opening buy. The user has now reported this same shape TWICE: first as
 * two supply trucks (PIPELINE 57(a)), then as two medics (57(b)). It is one defect with two instances, and
 * it reproduces for any type given a bare floor.
 *
 * The distinction that matters: a floor on a type whose value is INDEPENDENT of army size (one scout, one
 * capturer for a specific building) is legitimately flat. A floor on a SUPPORT type — one whose value is
 * PROPORTIONAL to the force it serves — must not be flat, because a flat floor asserts the support is needed
 * before the supported force exists. That is precisely the t=0 case.
 *
 * WHAT IT DOES WHEN THE DENOMINATOR IS ZERO — the question the whole bug turns on: the floor is ZERO. A
 * support unit with nothing to support has no floor at all. It is not "at least one anyway"; the ratio is
 * allowed to round down to nothing, and the type is then left to the ordinary deficit argmax like anything
 * else. That is the entire fix, and every other property here follows from it.
 *
 * The flat floor is retained as the CAP on the ratio rather than deleted, so an existing UnitFloors entry
 * still bounds the standing population from above and a ratio can never balloon it.
 *
 * PRIOR ART, and this is a REGRESSION back to it rather than a new idea: CaptureSupplyMath.EffectiveFloor
 * already keys the technician floor to a demand denominator (reachable neutral money POIs) and additionally
 * clamps it with ClampFloorToArmyShare so capture demand yields to combat while the army is thin. The
 * general UnitFloors mechanism added later dropped BOTH the denominator and the clamp. The shapes are kept
 * deliberately similar; they are not merged because the two floors live in different modules and act on
 * different populations, and collapsing them would put a module-ordering dependency between them.
 *
 * DETERMINISM (influence-stack invariant): pure integer, zero RNG, no world/actor references — plain scalars
 * in and out, so it is a deterministic map from its arguments and NUnit-pinned without a game run
 * (SupportFloorMathTest).
 *
 * OFF-SWITCH CONTRACT: perSupported <= 0 (the default, an unconfigured type) returns the flat floor VERBATIM,
 * so every profile that does not opt in — including @stable, which sets no UnitFloors at all — keeps its
 * existing answer exactly.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	public static class SupportFloorMath
	{
		/// <summary>The standing floor to hold for a support type right now, given how much of the force it
		/// supports actually exists.
		///
		/// <paramref name="perSupported"/> is the ratio denominator — "one of these per N supported units",
		/// the user's "about one medic per 20-man squad". When it is &lt;= 0 the type has no ratio configured
		/// and <paramref name="flatFloor"/> is returned unchanged (the off-switch: every existing config keeps
		/// its current behaviour).
		///
		/// Otherwise the floor is <c>min(flatFloor, supportedCount / perSupported)</c> — it PHASES IN as the
		/// supported force grows and is capped by the flat floor so it can never exceed the standing
		/// population the designer already signed off on. Integer division floors, which is the intended
		/// rounding: with a ratio of 20 the first unit is warranted at 20 supported, not at 1.
		///
		/// <paramref name="supportedCount"/> at zero — no army yet — therefore yields ZERO, which is what
		/// stops a floored support type being the opening call-in. Negative inputs are clamped to 0 rather
		/// than trusted, since a miscounted denominator must never invent a floor.</summary>
		public static int EffectiveFloor(int flatFloor, int perSupported, int supportedCount)
		{
			if (flatFloor <= 0)
				return 0;

			// Not opted in ⇒ the flat floor verbatim. This is the byte-identity contract.
			if (perSupported <= 0)
				return flatFloor;

			if (supportedCount <= 0)
				return 0;

			var scaled = supportedCount / perSupported;
			return scaled < flatFloor ? scaled : flatFloor;
		}
	}
}
