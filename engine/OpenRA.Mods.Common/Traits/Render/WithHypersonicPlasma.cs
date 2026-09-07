#region Copyright & License Information
/*
 * WW3MOD hypersonic plasma / streak (2026-09-07).
 *
 * The user's ask, in their words: "hypersonic weapons causes plasma to form at the leading edge",
 * and a future beyond-hypersonic round should arrive "just as a glowing streak". Explicitly a
 * PER-WEAPON YAML knob, not a blanket render change — so this is a trait a missile opts into, and
 * an actor without it renders exactly as it did before.
 *
 * THE MECHANISM IS BORROWED FROM THE SUB-TICK SMOOTHING SEAM (SubTickMotionSmoothing, 2026-09-06),
 * but it is a different operation on it. Smoothing MOVES the actor's renderables along its last-tick
 * velocity by a wall-clock fraction; this DUPLICATES them along that same velocity at fixed spacings
 * and tints each copy. Same axis, same source vector, opposite verbs.
 *
 * Two consequences of that shared axis are worth stating because they are what makes this cheap:
 *
 *	 1. NO ANGLES. The offsets are integer multiples of the velocity vector itself, so nothing here
 *		touches WAngle and the counterclockwise convention cannot be got wrong. It also means the
 *		copies land on the missile's real on-screen path — whatever projection the renderer applies
 *		to a world offset, it applies the same one to the missile's own motion.
 *	 2. NO SUB-TICK CLOCK READ. SubTickMotionSmoothing is another IRenderModifier on the same actor,
 *		and the two compose correctly in EITHER order: if smoothing runs first this trait receives
 *		already-predicted renderables and hangs its copies off them; if it runs second it offsets
 *		body and copies together. So this type never reads SubTickClock, and
 *		SubTickClockIsNotSimulationStateTest's permitted-reader list stays at one entry.
 *
 * The shadow is excluded for free: WithShadow calls AsDecoration() on the renderable it prepends
 * (WithShadow.cs:63), and the !IsDecoration filter below is the same one WithColoredOverlay uses.
 * Without it a hypersonic missile would drag a glowing plasma sheath across the ground.
 *
 * PURELY VISUAL. Nothing here is read by the simulation and nothing here is synchronised.
 */
#endregion

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	// Separated from the actor so the two things a YAML author has to predict — where a copy lands
	// and how bright it is — can be pinned without a world. See WithHypersonicPlasmaTest.
	/// <summary>Placement and fade arithmetic for <see cref="WithHypersonicPlasma"/>.</summary>
	public static class WithHypersonicPlasmaMath
	{
		// Long arithmetic rather than WVec's int operators: the numerator is a velocity component
		// times a WDist read straight out of YAML, and conventions.md is explicit that bounding the
		// multiplied-through worst case is the caller's job. A 6144 component (the MaxStep ceiling)
		// against a four-digit spacing is comfortable in int and nowhere near it in long.
		/// <summary>Displacement from one plasma sample to the next, along <paramref name="velocity"/>.</summary>
		public static WVec Step(WVec velocity, WDist spacing)
		{
			var length = velocity.Length;
			if (length <= 0 || spacing.Length == 0)
				return WVec.Zero;

			return new WVec(
				(int)((long)velocity.X * spacing.Length / length),
				(int)((long)velocity.Y * spacing.Length / length),
				(int)((long)velocity.Z * spacing.Length / length));
		}

		// Sample 1 renders at exactly the alpha the author wrote in the colour, and the last one at
		// baseAlpha/samples. Stating the first end that way is deliberate: it makes the YAML number
		// mean something a reader can check against a frame, rather than being scaled by a count
		// they also chose.
		/// <summary>Alpha for the <paramref name="sample"/>-th of <paramref name="samples"/> copies.</summary>
		public static float Alpha(float baseAlpha, int sample, int samples)
		{
			if (samples <= 0 || sample < 1 || sample > samples)
				return 0f;

			return baseAlpha * (samples - sample + 1) / samples;
		}
	}

	[Desc("Draws tinted copies of this actor's sprites along the velocity it had over the last",
		"completed tick: AHEAD of the nose for the compression-heated plasma sheath a hypersonic",
		"body forms at its leading edge, and BEHIND it for the glowing wake a beyond-hypersonic one",
		"arrives as. The two are independent extents of one mechanism and can be used together or",
		"separately.",
		"PURELY VISUAL. The simulation is untouched — collision, detonation and the smoke trail all",
		"still happen at the real CenterPosition — and nothing here is synchronised.",
		"DEFAULT OFF. With both sample counts at their default 0 this trait draws nothing at all, so",
		"adding it to a template without also setting a count changes no pixel.",
		"COST: each sample redraws every non-decoration sprite the actor produced, once. Six leading",
		"plus eight trailing on a one-sprite missile is fifteen draws where there was one. That is",
		"nothing on the handful of missiles in the air at once, and would not be on a unit type.",
		"Worth having only on something that moves a visible fraction of a cell per tick — the same",
		"bar as SubTickMotionSmoothing, roughly 300 WDist/tick and up. Below that the copies overlap",
		"the body and read as a blur rather than a streak.")]
	public class WithHypersonicPlasmaInfo : ConditionalTraitInfo
	{
		[Desc("Number of tinted copies drawn AHEAD of the body, for the leading-edge plasma sheath.",
			"0 disables the leading edge entirely. This is the on/off switch for it; the colour's",
			"alpha controls brightness, not presence.")]
		public readonly int LeadingSamples = 0;

		[Desc("Distance between consecutive leading-edge copies. Tight by default: a stagnation-point",
			"sheath hugs the nose. 128 is an eighth of a cell, about 3 px at 100% zoom.")]
		public readonly WDist LeadingSpacing = new WDist(128);

		[Desc("Colour of the leading-edge plasma, replacing the sprite's own colours outright.",
			"The alpha is the brightness of the FIRST copy; later ones fade linearly from it.",
			"Default is the pale blue-white of ionised air at a stagnation point.")]
		public readonly Color LeadingColor = Color.FromArgb(150, 200, 235, 255);

		[Desc("Number of tinted copies drawn BEHIND the body, for the glowing wake.",
			"0 disables the streak entirely.")]
		public readonly int TrailingSamples = 0;

		[Desc("Distance between consecutive trailing copies. Wider than the leading default: the wake",
			"is stretched by the body's own motion, and the streak's total length is this times",
			"TrailingSamples.")]
		public readonly WDist TrailingSpacing = new WDist(384);

		[Desc("Colour of the trailing streak. Default is the orange of a cooling wake — hotter than",
			"the smoke trail it is drawn over, cooler than the leading edge.")]
		public readonly Color TrailingColor = Color.FromArgb(120, 255, 150, 60);

		[Desc("Colour overlaid on the BODY sprite itself, at zero offset. This is what turns",
			"'a missile with a bright trail' into 'little more than a glowing streak': the airframe",
			"stops reading as an airframe. Default alpha 0, i.e. the body is left alone.")]
		public readonly Color BodyColor = Color.FromArgb(0, 255, 255, 255);

		[Desc("Added to each copy's ZOffset, but never to the body's. Positive draws the plasma over",
			"the body, negative behind it. Left at 0 the copies sort purely on the world position",
			"they were offset to, which is what a physical glow would do.")]
		public readonly int ZOffset = 0;

		[Desc("Per-tick steps longer than this are treated as teleports rather than motion and",
			"produce no plasma at all, so a SetPosition does not smear a glowing streak across the",
			"map. Default matches SubTickMotionSmoothing's: 6144, about three times the fastest",
			"shipped missile. Raise it here too if you add something faster.")]
		public readonly WDist MaxStep = new WDist(6144);

		public override object Create(ActorInitializer init) { return new WithHypersonicPlasma(this); }
	}

	/// <summary>Draws a hypersonic plasma sheath and/or wake along an actor's last-tick velocity.</summary>
	public class WithHypersonicPlasma : ConditionalTrait<WithHypersonicPlasmaInfo>, IRenderModifier, ITick
	{
		readonly long maxStepSquared;
		readonly float3 leadingTint, trailingTint, bodyTint;
		readonly float leadingAlpha, trailingAlpha, bodyAlpha;

		// Deliberately NOT [Sync]. These are derived from synced positions and so are deterministic,
		// but they are view state with no simulation consumer, and the sibling trait's rule — that
		// nothing on this render path may become reachable from the sync hash — is worth keeping
		// uniform across both of them rather than reasoned about case by case.
		WPos lastPosition;
		bool hasLastPosition;
		WVec velocity;

		public WithHypersonicPlasma(WithHypersonicPlasmaInfo info)
			: base(info)
		{
			maxStepSquared = (long)info.MaxStep.Length * info.MaxStep.Length;

			leadingTint = new float3(info.LeadingColor.R, info.LeadingColor.G, info.LeadingColor.B) / 255f;
			leadingAlpha = info.LeadingColor.A / 255f;
			trailingTint = new float3(info.TrailingColor.R, info.TrailingColor.G, info.TrailingColor.B) / 255f;
			trailingAlpha = info.TrailingColor.A / 255f;
			bodyTint = new float3(info.BodyColor.R, info.BodyColor.G, info.BodyColor.B) / 255f;
			bodyAlpha = info.BodyColor.A / 255f;
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled)
			{
				hasLastPosition = false;
				velocity = WVec.Zero;
				return;
			}

			var pos = self.CenterPosition;

			// ITick runs after every actor's activities in the same World.Tick, so this delta is
			// exactly one tick of travel. On the actor's first tick there is no previous position,
			// hence no velocity, hence no plasma — a missile draws clean on the frame it spawns.
			var step = hasLastPosition ? pos - lastPosition : WVec.Zero;
			velocity = step.LengthSquared > maxStepSquared ? WVec.Zero : step;

			lastPosition = pos;
			hasLastPosition = true;
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			// A paused world still renders while World.Tick returns without moving anything, so the
			// velocity below is whatever the last advancing tick left behind. Freezing the plasma
			// with the missile is the honest reading of a paused frame; SubTickMotionSmoothing stops
			// at the same expression for the same reason.
			if (IsTraitDisabled || velocity == WVec.Zero || !self.World.SimulationIsAdvancing)
				return r;

			var leading = Info.LeadingSamples > 0
				? WithHypersonicPlasmaMath.Step(velocity, Info.LeadingSpacing) : WVec.Zero;
			var trailing = Info.TrailingSamples > 0
				? WithHypersonicPlasmaMath.Step(velocity, Info.TrailingSpacing) : WVec.Zero;

			if (leading == WVec.Zero && trailing == WVec.Zero && bodyAlpha <= 0f)
				return r;

			return ModifiedRender(r, leading, trailing);
		}

		IEnumerable<IRenderable> ModifiedRender(IEnumerable<IRenderable> r, WVec leading, WVec trailing)
		{
			foreach (var a in r)
			{
				yield return a;

				// !IsDecoration is what keeps the shadow, the health bar and the selection box out of
				// this. WithShadow marks its renderable AsDecoration, so a glowing copy of it never
				// gets made — see the file header.
				if (a.IsDecoration || a is not IModifyableRenderable ma)
					continue;

				if (bodyAlpha > 0f)
					yield return Tinted(ma, bodyTint, bodyAlpha);

				if (leading != WVec.Zero)
					for (var i = 1; i <= Info.LeadingSamples; i++)
						yield return Tinted(ma, leadingTint,
							WithHypersonicPlasmaMath.Alpha(leadingAlpha, i, Info.LeadingSamples))
							.OffsetBy(leading * i);

				if (trailing != WVec.Zero)
					for (var i = 1; i <= Info.TrailingSamples; i++)
						yield return Tinted(ma, trailingTint,
							WithHypersonicPlasmaMath.Alpha(trailingAlpha, i, Info.TrailingSamples))
							.OffsetBy(-trailing * i);
			}
		}

		// ReplaceColor rather than a multiply: the shader substitutes the colour outright, so every
		// pixel of the airframe becomes one flat plasma colour at this alpha instead of a tinted
		// picture of a missile. Same call WithColoredOverlay and WithShadow make.
		IRenderable Tinted(IModifyableRenderable ma, float3 tint, float alpha)
		{
			var tinted = ma.WithTint(tint, ma.TintModifiers | TintModifiers.ReplaceColor).WithAlpha(alpha);
			return Info.ZOffset != 0 ? tinted.WithZOffset(ma.ZOffset + Info.ZOffset) : tinted;
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			// Unmodified, following SubTickMotionSmoothing and WithShadow's ZOffset handling. Bounds
			// feed selection and the ScreenMap's culling partition; these actors are RejectsOrders and
			// nobody clicks them. The cost is that a missile can be culled while a trailing copy would
			// still have been on screen — at TrailingSpacing 384 x 8 that is three cells of streak
			// clipped at the viewport edge, on an actor that is about to leave it anyway.
			return bounds;
		}
	}
}
