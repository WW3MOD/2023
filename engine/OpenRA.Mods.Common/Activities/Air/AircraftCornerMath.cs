#region Copyright & License Information
/*
 * WW3MOD helicopter corner geometry (pure math) — how far short of an INTERMEDIATE waypoint a CanSlide
 * airframe should drop that waypoint and pick up the next leg, so it turns through the corner as an arc at
 * speed instead of braking to a stop on the point.
 *
 * WHY THIS IS NOT THE TEXTBOOK CORNER-CUT. The usual answer for a vehicle rounding a corner is the tangent
 * distance to a constant-radius arc, r*tan(theta/2) with r = v^2/a. That is the wrong curve for this engine.
 * A CanSlide aircraft is not steered; it is a point mass whose velocity is driven straight at a target
 * velocity. Aircraft.CalculateAccelerationToWaypoint returns MaxAcceleration in the direction
 * (desiredVelocity - CurrentVelocity), and desiredVelocity for a pure move leg is the FULL speed vector at
 * the next waypoint. Because the target velocity is constant over the corner, the acceleration is always
 * parallel to the same difference vector, so in VELOCITY space the tip travels a straight CHORD from v_in to
 * v_out, shortening at exactly MaxAcceleration per tick. It is not a circular arc and it never had a
 * constant radius: the airframe slows through the corner and speeds back up, which is what a helicopter
 * actually does and why tan() overshoots the required lead badly at large deflections.
 *
 * THE DERIVATION, for a turn of theta at speed v with acceleration a per tick:
 *
 *   |v_out - v_in| = 2*v*sin(theta/2)                      (chord between two equal-length vectors)
 *   T              = 2*v*sin(theta/2) / a                  (ticks to traverse it, at a per tick)
 *   mean velocity  = (v_in + v_out)/2, magnitude v*cos(theta/2), pointing along the BISECTOR
 *   displacement   = T * v * cos(theta/2)                  (along the bisector)
 *
 * Releasing d short of the corner and rejoining d along the outbound leg displaces the airframe by
 * d*(u_in + u_out), whose magnitude is 2*d*cos(theta/2) and which also lies on the bisector. Equating:
 *
 *   2*d*cos(theta/2) = T * v * cos(theta/2)   ->   d = v*T/2 = (v^2 / a) * sin(theta/2)
 *
 * So the release distance is sin(theta/2), NOT tan(theta/2), and it is bounded by v^2/a at any deflection
 * rather than diverging at 180 degrees. The cos(theta/2) cancels, which is why the result is exact rather
 * than a small-angle approximation.
 *
 * SEMI-IMPLICIT EULER IS EXACT HERE, WHICH IS A HAPPY ACCIDENT WORTH RECORDING. Aircraft.Tick adds the
 * acceleration and THEN moves by the new velocity, so the summed displacement is
 * T*(v_in + v_out)/2 + (v_out - v_in)/2 rather than the continuous T*(v_in + v_out)/2. The extra half-step
 * (v_out - v_in)/2 is the chord direction, which is PERPENDICULAR to the bisector for two equal-length
 * vectors — so it contributes exactly zero along the bisector and the formula above needs no correction.
 * It does contribute off-axis: the closed form says the airframe rejoins the outbound leg offset laterally
 * by about v*sin(theta/2), a one-tick-scale artefact of ~0.17 cells for a HELI at 245 taking a right-angle
 * corner. DO NOT USE THAT AS A BOUND. Simulating the real integer path (tools/heli-corner-model/model.py)
 * measures 516 WDist, ~0.50 cells — 3.0x the closed form — at the shipped default. The closed form
 * under-predicts and the reason is NOT established; see WORKSPACE/DISCOVERIES.md, which records the
 * measurement and explicitly declines to name a mechanism. Any lateral-deviation threshold must be taken
 * from the model, not from v*sin(theta/2).
 *
 * WHY THE COUNTERCLOCKWISE WAngle CONVENTION CANNOT INVERT THIS. Everything here consumes
 * WAngle.AngleDiff, a MAGNITUDE in [0, 512]. There is no signed turn direction anywhere in this file, so
 * the sign convention that inverts hand-rolled turn maths has nothing to bite. Which way the airframe
 * rotates is decided by the acceleration vector in Aircraft.Tick, not here.
 *
 * DETERMINISM: pure integer arithmetic, zero random draws, no collection iteration, no floating point.
 * Deliberately so — Aircraft.CalculateAccelerationToWaypoint still runs a double through Math.Sqrt inside
 * the synchronised simulation (Aircraft.cs:464), and Aircraft.Tick a single-precision divide at :521;
 * this file does not add a third such site. That
 * pre-existing float is recorded in WORKSPACE/DISCOVERIES.md; it is not touched here because changing it is
 * a live behaviour change to every helicopter approach and belongs in its own branch.
 */
#endregion

namespace OpenRA.Mods.Common.Activities
{
	public static class AircraftCornerMath
	{
		/// <summary><para>Unsigned deflection between the leg being flown and the leg queued after it — 0 when the
		/// two are collinear, 512 for a full reversal. This is the theta of the header derivation.</para>
		///
		/// <para>A magnitude, not a signed turn: see the header on why the counterclockwise WAngle convention
		/// cannot invert anything computed here.</para></summary>
		public static WAngle Deflection(WAngle inboundYaw, WAngle outboundYaw)
		{
			return WAngle.AngleDiff(inboundYaw, outboundYaw);
		}

		/// <summary><para>Whether an intermediate waypoint is eligible to be released early at all. False means the
		/// caller keeps the current behaviour verbatim: decelerate onto the point and stop.</para>
		///
		/// <para>Three ways to be ineligible, and the first two are the load-bearing ones:</para>
		///
		/// <para>NO NEXT LEG. A terminal waypoint has nothing to arc towards, so it must still brake and stop
		/// exactly on the point. This is the case the user reports already works and it is the one this whole
		/// change must not regress.</para>
		///
		/// <para>FEATURE OFF. <paramref name="aggressionPercentage"/> at or below 0 restores the pre-change path
		/// exactly, which is the off-switch for the behaviour and the control arm for measuring it.</para>
		///
		/// <para>TOO SHARP. Past <paramref name="maxDeflection"/> there is no arc worth flying. The derivation
		/// stays finite at 180 degrees but stops being meaningful: at a true reversal the bisector displacement
		/// is zero, the airframe decelerates to a stop and accelerates back out, and releasing early just means
		/// it turns around short of the point the player clicked. Braking normally is both more honest and what
		/// a pilot does.</para></summary>
		public static bool ShouldReleaseEarly(bool hasNextLeg, int aggressionPercentage, WAngle deflection, WAngle maxDeflection)
		{
			return hasNextLeg && aggressionPercentage > 0 && deflection.Angle <= maxDeflection.Angle;
		}

		/// <summary><para>Distance short of an intermediate waypoint at which to drop it and take up the next leg:
		/// <c>(speed^2 / maxAcceleration) * sin(deflection/2)</c>, scaled by
		/// <paramref name="aggressionPercentage"/>. Derivation and why it is sin and not tan: file header.</para>
		///
		/// <para><paramref name="speed"/> is the airframe's CURRENT horizontal speed in WDist per tick, not its
		/// rule-book maximum — a helicopter that is still spooling up needs a shorter lead, and using the live
		/// value makes that fall out for free.</para>
		///
		/// <para>FLOORED AT ONE TICK OF TRAVEL, and this floor is the entire reason the feature is safe to ship.
		/// At a near-zero deflection the geometric distance is near zero, so a straight-through waypoint would be
		/// approached at full speed with no release — which is precisely the arrangement that was removed in
		/// 02006314 for snapping the airframe to a dead stop on arrival. Releasing once the waypoint is inside a
		/// single tick's travel means the airframe hands off the tick before it would have overshot, so the
		/// arrival-at-speed case that produced the snap no longer exists. Do not remove this floor without
		/// reading that commit.</para>
		///
		/// <para>The floor is <c>speed + maxAcceleration</c>, not <c>speed</c>, because Aircraft.Tick is
		/// semi-implicit: it adds the acceleration and THEN moves by the new velocity, so a tick can cover up to
		/// one acceleration step more than the speed read at its start. At a plain <c>speed</c> floor there is a
		/// window of exactly one acceleration step where the airframe slips past the waypoint before releasing,
		/// takes a single tick of the overshoot branch's emergency brake and releases on the tick after. That is
		/// about 4% of one tick's speed at the live helicopter config and would never be seen — but it is the
		/// old failure mode in miniature, and it costs one addition to make it unreachable instead of
		/// negligible.</para>
		///
		/// <para>CAPPED AT HALF THE LEG, applied before the floor. The geometric distance is 4243 WDist, about
		/// 4.1 cells, for a right-angle corner at a HELI's 245 — longer than plenty of legs a player will draw.
		/// (5.9 cells is v^2/a, the value BEFORE the sin(45 deg) factor; an earlier revision of this comment
		/// reasoned the cap against that larger number.) Without a
		/// cap the release fires on the leg's FIRST tick, the waypoint is dropped before the airframe has flown
		/// any of it, and a chain of closely-spaced waypoints collapses in a single tick into a straight run at
		/// the last one — the path silently not being flown at all. Half the leg guarantees every waypoint is
		/// approached. A corner that wants more lead than that is simply too tight to take at this speed: the
		/// airframe arcs wide of the outbound line rather than stopping, which is what a helicopter does and is
		/// the behaviour being asked for.</para>
		///
		/// <para>The floor is applied AFTER the cap and therefore wins outright on a very short leg. That
		/// ordering is deliberate: the cap is about flying the path, the floor is about never arriving at an
		/// intermediate waypoint at speed, and only the second one is a correctness guarantee.</para>
		///
		/// <para>Widened to long for the intermediate product: speed^2 * sin * percentage reaches ~7.2e9 at the
		/// fastest shipped helicopter and would overflow int.</para>
		///
		/// <para>PRECONDITION: <c>maxAcceleration &gt; 0</c> — it is an Aircraft Info field with a positive
		/// default and no shipped override. Zero divides, loudly, which is the intent.</para></summary>
		public static int ReleaseDistance(int speed, int maxAcceleration, WAngle deflection, int aggressionPercentage, int legLength)
		{
			if (speed <= 0)
				return 0;

			var sinHalf = new WAngle(deflection.Angle / 2).Sin();
			var geometric = (int)((long)speed * speed * sinHalf * aggressionPercentage
				/ ((long)maxAcceleration * 1024 * 100));

			var cap = legLength / 2;
			if (geometric > cap)
				geometric = cap;

			var floor = speed + maxAcceleration;
			return geometric > floor ? geometric : floor;
		}
	}
}
