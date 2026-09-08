#region Copyright & License Information
/*
 * WW3MOD motion endpoint (2026-09-08).
 *
 * A view-only consumer that draws a mover AHEAD of its last simulated position -- see
 * SubTickMotionSmoothing and WithHypersonicPlasma -- has one question it cannot answer from the
 * actor alone: how far is this thing still going to travel? Without an answer it extrapolates a
 * full tick every tick, including the last one, and draws a missile through the target it is about
 * to detonate on.
 *
 * "Is this the final tick" would be the other way to ask it, and it is the worse one: the renderer
 * would have to know something about the mover's schedule, and every mover would have to agree on
 * what a final tick is. The DISTANCE LEFT is the same information in a form the renderer can use
 * directly -- it clamps an offset, which is exactly the operation being performed -- and a mover
 * that does not know where it stops answers null rather than lying.
 */
#endregion

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// A mover that knows the position it will come to rest at, so a view-only consumer can bound
	/// how far past the actor's real position it is allowed to draw.
	/// </summary>
	public interface IMotionEndpoint
	{
		/// <summary>
		/// The position beyond which this actor will not travel under its current orders, or null
		/// when there is no such position -- nothing has been ordered yet, or the mover genuinely
		/// does not know. Null means "unbounded", so a consumer clamping against it must treat that
		/// as "no clamp" rather than as zero.
		/// </summary>
		WPos? MotionEndpoint { get; }
	}
}
