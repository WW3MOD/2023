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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>
	/// Drives an empty unit toward a mobile SupplyProvider host (e.g. supply truck)
	/// with supply remaining, re-picks the target if it runs out of supply mid-route,
	/// and shows a target line. Used by AmmoPool.AutoRearm when the picked resupplier
	/// is a SupplyProvider host without a docking gate (i.e. a truck or ground cache,
	/// not a Logistics Center).
	/// </summary>
	public class SeekSupplyProvider : Activity
	{
		readonly IMove move;
		readonly IMoveInfo moveInfo;
		readonly AmmoPool[] pools;

		/// <summary>
		/// <para>
		/// True when this errand was the unit's OWN idea, taken because the unit had run dry —
		/// AmmoPool.AutoRearmIfDry and the dispatchers that share its OutOfEssentialAmmo predicate.
		/// False for the player's explicit Resupply order, which is a destination order and must run
		/// to full wherever it was aimed.
		/// </para>
		/// <para>
		/// Two things follow from it, and they are one rule — an errand nobody ordered lasts exactly
		/// as long as the reason for it: the walk ENDS once the unit can fight again, and it ends by
		/// walking BACK to where it set off from rather than stopping wherever the news arrived.
		/// </para>
		/// <para>
		/// "Can fight again" is deliberately not a new definition: it is AmmoPool.OutOfEssentialAmmo
		/// negated, the exact condition that dispatched us. Reusing THE SAME FUNCTION — not a second
		/// test that happens to agree — is what makes the errand non-oscillating, and it is also what
		/// keeps it from being stillborn. When the dispatch predicate widened past AllPoolsEmpty on
		/// 2026-08-21, an exit still reading AllPoolsEmpty would have been false at the very moment a
		/// partially-dry unit was dispatched: the errand would be pointless on its first tick and the
		/// unit would take one step and stop. Dispatch and exit move together or not at all.
		/// No dispatcher keyed on OutOfEssentialAmmo can re-fire the instant we quit,
		/// and the one that can (AutoSeekSupplies' idle seek, keyed on BelowSeekThreshold) queues
		/// SeekSuppliesAndReturn, which holds until FULL and so cannot ping-pong against this.
		/// </para>
		/// </summary>
		readonly bool dispatchedBecauseDry;

		Actor currentTarget;
		CPos origin;
		bool returning;
		bool moveQueued;
		int retargetTicks;

		const int RetargetInterval = 25;

		// Cells. The origin can be occupied by the time we get back, so settle for close by.
		// Matches SeekSuppliesAndReturn, which runs the same leg for the idle-seek errand.
		const int HomeNearEnough = 2;

		public SeekSupplyProvider(Actor self, Actor initialTarget, bool dispatchedBecauseDry)
		{
			move = self.Trait<IMove>();
			moveInfo = self.Info.TraitInfo<IMoveInfo>();
			pools = self.TraitsImplementing<AmmoPool>().ToArray();
			currentTarget = initialTarget;
			this.dispatchedBecauseDry = dispatchedBecauseDry;

			// PITFALL: an activity that re-evaluates its own reason inside Tick MUST set this false.
			// With the default (true) Activity.TickOuter runs `TickChild(self) && (finishing ||
			// Tick(self))` (Activity.cs:112), so Tick is skipped entirely for as long as a child is
			// alive — a body full of per-tick checks then only runs once the move has already
			// finished, which reads as "the unit ignores everything until it arrives".
			ChildHasPriority = false;
		}

		protected override void OnFirstRun(Actor self)
		{
			// The cell to come back to. Read here rather than in the constructor because AutoRearm
			// queues us with QueueActivity(false, …), which CANCELS the pre-empted order and leaves
			// us sitting BEHIND it — the unit is still finishing that cell when we are constructed.
			// This is also the only honest reading of "where it came from": the pre-empted order
			// itself is gone, not paused (Activity.Cancel nulls NextActivity, Activity.cs:198), so
			// there is nothing to resume and the origin cell is all that survives.
			origin = self.Location;
		}

		/// <summary>
		/// <para>Is this host still worth walking to — which is "can it pay for a batch of something we
		/// still want", not "does it have anything left in it". Those differ over the whole band from 1
		/// supply up to one batch price, and that band is where this activity used to strand people: a
		/// host in it is stocked, so a CurrentSupply &gt; 0 test keeps the unit walking, and it can never
		/// serve, so the unit arrives and parks in the in-range branch below waiting for a push that
		/// cannot come.</para>
		///
		/// <para>Shares AmmoPool.HostCanAffordSomethingWeNeed with the dispatchers rather than restating
		/// the comparison, so a host they would refuse to send us to is one we also stop walking to.</para>
		/// </summary>
		bool TargetValid(Actor a)
		{
			if (a == null || a.IsDead || !a.IsInWorld)
				return false;

			var sp = a.TraitOrDefault<SupplyProvider>();
			return sp != null && AmmoPool.HostCanAffordSomethingWeNeed(a, pools);
		}

		/// <summary>
		/// <para>The retarget pick, and it must apply the SAME affordability test the dispatcher used.
		/// This runs every RetargetInterval ticks for the whole journey, so a looser test here silently
		/// undoes an affordable dispatch one layer down: sent to the cache that can pay, the unit
		/// retargets onto a nearer one that cannot and walks to it instead. Fixing only the choosers
		/// leaves that hole open.</para>
		/// </summary>
		Actor FindBest(Actor self)
		{
			var rearmInfo = self.Info.TraitInfoOrDefault<RearmableInfo>();
			if (rearmInfo == null)
				return null;

			return self.World.ActorsHavingTrait<SupplyProvider>()
				.Where(a => !a.IsDead && a.IsInWorld
					&& a.Owner == self.Owner
					&& rearmInfo.RearmActors.Contains(a.Info.Name)
					&& AmmoPool.HostCanAffordSomethingWeNeed(a, pools))
				.ClosestToIgnoringPath(self);
		}

		/// <summary>The errand has served its purpose or lost its reason; nothing more to walk for.</summary>
		bool ErrandIsPointless()
		{
			if (pools.All(p => p.HasFullAmmo))
				return true;

			return AmmoPool.SelfAssignedErrandIsOver(dispatchedBecauseDry, pools);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			// A child cancelled by a previous leg has to unwind before the next one is planned:
			// QueueChild APPENDS to the child chain (Activity.cs:220-226), so queuing now would run
			// the stale move first and walk us back the way we came.
			if (!moveQueued && ChildActivity != null)
			{
				TickChild(self);
				return false;
			}

			if (returning)
			{
				if (!moveQueued)
				{
					QueueChild(move.MoveTo(origin, HomeNearEnough, targetLineColor: AutomaticOrder.LineColor));
					moveQueued = true;
				}

				TickChild(self);

				// Done when the walk home finishes — including the case where the origin cell was
				// taken while we were away and MoveTo settled for a nearby one.
				return ChildActivity == null;
			}

			// Nothing to rearm. Never dispatched for a good reason, so there is no station to hold.
			if (pools.Length == 0)
				return true;

			if (ErrandIsPointless())
				return BeginReturn(self);

			// Re-pick target if current one is invalid (dead, empty) or periodically.
			if (!TargetValid(currentTarget) || --retargetTicks <= 0)
			{
				retargetTicks = RetargetInterval;
				var newTarget = FindBest(self);

				if (newTarget != currentTarget)
				{
					currentTarget = newTarget;

					if (ChildActivity != null)
					{
						// Do NOT plan the new approach in this same tick. QueueChild appends, so the
						// move being cancelled here would run first and walk us to the OLD target
						// anyway. Hand back to the unwind guard above; it re-plans once it has let go.
						ChildActivity.Cancel(self);
						moveQueued = false;
						return false;
					}
				}
			}

			if (currentTarget == null)
			{
				// No supply available anywhere — flag for pickup and exit.
				foreach (var p in pools)
					p.NeedsResupply = true;

				return BeginReturn(self);
			}

			var sp = currentTarget.Trait<SupplyProvider>();
			var rearmRange = sp.Info.Range;
			var distSq = (currentTarget.CenterPosition - self.CenterPosition).HorizontalLengthSquared;

			if (distSq <= rearmRange.LengthSquared)
			{
				// In range — let the SupplyProvider push ammo. Stay put.
				if (ChildActivity != null && moveQueued)
				{
					ChildActivity.Cancel(self);
					moveQueued = false;
				}

				return false;
			}

			// Out of range — move within rearm range.
			if (!moveQueued)
			{
				QueueChild(move.MoveWithinRange(Target.FromActor(currentTarget), rearmRange,
					targetLineColor: AutomaticOrder.LineColor));
				moveQueued = true;
			}

			TickChild(self);
			return false;
		}

		/// <summary>
		/// Switch to the walk home, or just stop if the errand was ordered rather than self-assigned.
		/// Returns what Tick should return.
		/// </summary>
		bool BeginReturn(Actor self)
		{
			if (!dispatchedBecauseDry)
				return true;

			returning = true;
			currentTarget = null;

			if (ChildActivity != null)
				ChildActivity.Cancel(self);

			// The unwind guard at the top of Tick plans the return leg once the cancelled approach
			// has finished the cell it was crossing.
			moveQueued = false;
			return false;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (returning)
				yield return new TargetLineNode(Target.FromCell(self.World, origin), AutomaticOrder.LineColor);
			else if (currentTarget != null && !currentTarget.IsDead && currentTarget.IsInWorld)
				yield return new TargetLineNode(Target.FromActor(currentTarget), AutomaticOrder.LineColor);
		}
	}
}
