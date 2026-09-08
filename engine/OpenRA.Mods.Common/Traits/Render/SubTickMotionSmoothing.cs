#region Copyright & License Information
/*
 * WW3MOD sub-tick motion smoothing (2026-09-06).
 *
 * Reported as "fast flying missiles make it obvious they are following the ticks, so it looks very
 * low fps". The screen is not slow — rendering is uncapped by default and Game.Loop forces at least
 * one frame per logic tick — the POSITIONS are. At Timestep 60 the world moves 16.67 times a second,
 * and KinzhalMissile covers 2000 WDist (just under two cells, ~47 px at 100% zoom) per step.
 *
 * This draws the sprite AHEAD of its last simulated position, along the velocity it had over the
 * last completed tick, scaled by how far through the current tick the frame is. PREDICTION, not
 * interpolation: interpolating between the two most recent positions would be geometrically exact
 * but would draw everything a full tick in the past, which was rejected.
 *
 * BOUNDED BY THE DISTANCE LEFT TO TRAVEL (2026-09-08). Reported as "it makes the missile look like
 * it flies through the target a bit before the explosion registers". Prediction assumes the next
 * tick moves the actor the way the last one did, and on the last tick of a flight that assumption
 * is not merely imprecise, it is false: BallisticMissileFly places the missile ON the target, then
 * spends one more tick finishing before the Kill lands (BallisticMissileFly.cs:206-210). Through
 * that whole inter-tick interval the trait still held the arrival step -- up to 2400 WDist on a
 * Kinzhal in its terminal dive -- so the sprite slid a full ~56 px past the impact point and
 * snapped back to explode behind itself.
 *
 * The clamp is the REMAINING DISTANCE, not a test for the final tick, and that distinction is the
 * whole design: a remaining distance also fixes the tick BEFORE arrival, where the missile has less
 * left to travel than its last step was long and the prediction ran past the target mid-tick. See
 * IMotionEndpoint.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	/// <summary>The arithmetic, separated from the actor so it can be tested without a world.</summary>
	public static class SubTickMotionSmoothingMath
	{
		/// <summary>
		/// How far ahead of its last simulated position a mover should be DRAWN, never further than
		/// it has left to travel.
		/// </summary>
		/// <param name="velocity">Displacement over the last completed tick.</param>
		/// <param name="fraction">Elapsed portion of the current tick, in SubTickClock.One-ths.</param>
		/// <param name="strength">Percentage of the full prediction to apply.</param>
		/// <param name="remaining">
		/// Distance from the last simulated position to the point the mover stops at, or null when
		/// nothing knows where that is. See <see cref="IMotionEndpoint"/>.
		/// </param>
		public static WVec Offset(WVec velocity, int fraction, int strength, WDist? remaining)
		{
			var offset = Extrapolate(velocity, fraction, strength);
			// Null is "unbounded", not "zero": a mover with no known endpoint is smoothed exactly as
			// it was before this clamp existed. Nothing that inherits ^ShootableMissile is in that
			// case -- BallisticMissile answers with the impact point of the flight it is running.
			if (remaining == null)
				return offset;

			var left = remaining.Value.Length;
			if (left <= 0)
				return WVec.Zero;

			var length = offset.Length;
			if (length <= left)
				return offset;

			// Long arithmetic: the numerator is an offset component -- bounded by MaxStep in the
			// trait, but MaxStep is a YAML field -- times a distance that can be the length of the
			// map. conventions.md is explicit that bounding the multiplied-through worst case is
			// the caller's job.
			return new WVec(
				(int)((long)offset.X * left / length),
				(int)((long)offset.Y * left / length),
				(int)((long)offset.Z * left / length));
		}

		// PRIVATE, and that is the fix rather than an implementation detail of it. The unclamped
		// prediction is what drew a missile through its own target, so it has no caller outside this
		// method: `Offset` WRAPS it instead of taking its result, which is what makes the overshoot
		// unreachable rather than merely un-hit. See NoOffsetIsComputedWithoutAnImpactLimit.
		static WVec Extrapolate(WVec velocity, int fraction, int strength)
		{
			if (strength <= 0)
				return WVec.Zero;

			// Clamped rather than trusted. At fraction 0 (the frame right after a tick) this is
			// exactly zero, and at SubTickClock.One it is exactly one tick of travel — those two
			// bounds are the whole safety argument for the feature, so they are enforced here rather
			// than left to the caller.
			var f = fraction.Clamp(0, SubTickClock.One);

			// Two steps, not velocity * (f * strength) / (One * 100): a single product would be
			// 1024 * 100 = 102400 times a component that is already tens of thousands of WDist on a
			// long shot, which overflows int well before the division brings it back.
			return velocity * f / SubTickClock.One * strength / 100;
		}
	}

	[Desc("Renders this actor ahead of its last simulated position, projected along the velocity it",
		"had over the last completed tick, so a fast mover glides between logic ticks instead of",
		"stepping between them. PURELY VISUAL: nothing here is read by the simulation, and the",
		"actor's real CenterPosition — which is what it collides, detonates and leaves a trail at —",
		"is untouched.",
		"Worth having only on things that move a visible fraction of a cell per tick. At the mod's",
		"Timestep 60 that means roughly 300 WDist/tick and up; a 100 WDist/tick tank steps 2.3 px and",
		"has nothing to smooth.",
		"OVERSHOOTS ON DIRECTION CHANGE, by at most one tick of travel, because it extrapolates the",
		"last velocity rather than reading the next one. Harmless on a ballistic arc, where the",
		"velocity turns by a fraction of a degree per tick; visible on anything that can reverse or",
		"stop dead in a single tick.",
		"NEVER DRAWS PAST THE END OF THE JOURNEY. If the actor carries a trait answering",
		"IMotionEndpoint — BallisticMissile does, with the impact point of the flight it is running —",
		"the prediction is clamped to the distance still left, so the sprite reaches the endpoint and",
		"stops there instead of sliding through it and snapping back. An actor with no such trait is",
		"unbounded and smoothed exactly as it was before.")]
	public class SubTickMotionSmoothingInfo : ConditionalTraitInfo
	{
		[Desc("Percentage of the predicted offset to apply. 100 = draw a full tick ahead at the",
			"instant before the next tick lands. Lower values trade smoothness back for a smaller",
			"overshoot on direction change; 0 disables the trait without removing it.")]
		public readonly int Strength = 100;

		[Desc("Per-tick steps longer than this are treated as teleports rather than motion, and",
			"produce no prediction at all.",
			"This exists for SetPosition: BallisticMissileFly ends a flight by placing the actor on",
			"the target and only then killing it, and a spawner can drop an actor anywhere. Without",
			"the guard a single-tick jump of a hundred cells would be extrapolated forward again.",
			"Default is 6144 (6 cells), about three times the fastest shipped mover — KinzhalMissile",
			"at 2000 WDist/tick, 2400 in its terminal dive. Raise it if you add something faster, or",
			"that actor silently stops being smoothed.")]
		public readonly WDist MaxStep = new WDist(6144);

		public override object Create(ActorInitializer init) { return new SubTickMotionSmoothing(this); }
	}

	public class SubTickMotionSmoothing : ConditionalTrait<SubTickMotionSmoothingInfo>, IRenderModifier, ITick
	{
		readonly long maxStepSquared;

		// Deliberately NOT [Sync]. These are derived from synced positions and so are themselves
		// deterministic, but nothing may hash them: the moment one is sync-hashed, the fraction they
		// are multiplied by in ModifyRender becomes reachable from the sync hash, and that fraction
		// is wall-clock. See SubTickClockIsNotSimulationStateTest.
		WPos lastPosition;
		bool hasLastPosition;
		WVec velocity;

		// Where the mover stops, and how far that is from the position `velocity` arrived at. Looked
		// up once (the trait set is fixed for an actor's life) and recomputed once a tick, so
		// ModifyRender stays a single call into the math with nothing to get wrong.
		IMotionEndpoint endpoint;
		WDist? remaining;

		public SubTickMotionSmoothing(SubTickMotionSmoothingInfo info)
			: base(info)
		{
			maxStepSquared = (long)info.MaxStep.Length * info.MaxStep.Length;
		}

		protected override void Created(Actor self)
		{
			base.Created(self);

			// TraitOrDefault, not Trait: this trait is generic and an actor is allowed to have no
			// idea where it is going. That case is `remaining == null`, which the math reads as
			// unbounded.
			endpoint = self.TraitOrDefault<IMotionEndpoint>();
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled)
			{
				// Forget the history rather than keeping it. Re-enabling with a stale position would
				// extrapolate from a step that spans however long the trait was off.
				hasLastPosition = false;
				velocity = WVec.Zero;
				remaining = null;
				return;
			}

			var pos = self.CenterPosition;

			// ITick runs AFTER every actor's activities in the same World.Tick (World.cs:506-508), so
			// `pos` is the end-of-tick position and the delta below is exactly one tick of travel.
			//
			// hasLastPosition is false on an actor's FIRST tick, which is the spawn case the whole
			// guard exists for: there is no previous position, so there is no velocity, so the offset
			// is zero and the missile is drawn exactly where it was created.
			var step = hasLastPosition ? pos - lastPosition : WVec.Zero;
			velocity = step.LengthSquared > maxStepSquared ? WVec.Zero : step;

			// Measured from `pos`, which is where the offset is applied from, so the two are the same
			// journey. On the tick a ballistic missile arrives this is zero and the prediction is
			// switched off for the interval that used to draw the overshoot.
			var end = endpoint?.MotionEndpoint;
			remaining = end.HasValue ? new WDist((end.Value - pos).Length) : (WDist?)null;

			lastPosition = pos;
			hasLastPosition = true;
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			// A paused world still renders — Game.Loop keeps calling LogicTick and SubTickClock keeps
			// sweeping — while World.Tick returns without moving anything. Extrapolating through that
			// makes a paused missile slide forward and snap back at the tick rate.
			if (IsTraitDisabled || velocity == WVec.Zero || !self.World.SimulationIsAdvancing)
				return r;

			// SubTickClock is read HERE and nowhere else in this trait, and the value is consumed into
			// a return rather than stored. That is the property SubTickClockIsNotSimulationStateTest
			// pins; moving this read into a helper Tick can also reach would defeat it.
			var offset = SubTickMotionSmoothingMath.Offset(velocity, SubTickClock.Fraction, Info.Strength, remaining);
			if (offset == WVec.Zero)
				return r;

			// Every renderable the actor produced, so body, shadow and anything attached move as one.
			return r.Select(a => a.OffsetBy(offset));
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			// Unmodified, following Hovers. Bounds feed selection and the ScreenMap's culling
			// partition, which is rebuilt per render tick — jittering them every frame would churn
			// that for no gain, since these actors are RejectsOrders and nobody clicks them. The cost
			// is that an actor can be culled up to one tick of travel (two cells on a Kinzhal) before
			// its PREDICTED sprite would have left the viewport edge.
			return bounds;
		}
	}
}
