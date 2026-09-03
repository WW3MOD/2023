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
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public enum CaptureDispatchState
	{
		/// <summary>
		/// Not capturable, or the player owns no capture unit that could ever take it. Callers fall
		/// through to stock behaviour: with nothing to dispatch there is nothing being blocked, and a
		/// blocked cursor over every building would be noise for a player who owns no technicians.
		/// </summary>
		NotATarget,

		/// <summary>A free capture unit exists and can be sent.</summary>
		Available,

		/// <summary>
		/// The player owns capture units but every one of them is already committed elsewhere. This
		/// one does warrant a blocked cursor — the click would otherwise do nothing, silently.
		/// </summary>
		AllCapturersBusy,

		/// <summary>Capturable, but a capture unit is already on its way — sending another wastes one.</summary>
		AlreadyCovered
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Lets the player send capture units at a capturable structure without selecting one first.",
		"Add to the World actor. Absent, the right-click gesture and the Deploy command fall back to",
		"their stock behaviour, so mods that do not list this trait are unaffected.")]
	public class CaptureDispatchManagerInfo : TraitInfo
	{
		[CursorReference]
		[Desc("Cursor shown over a structure that a free capture unit can be sent at.")]
		public readonly string Cursor = "deploy";

		[CursorReference]
		[Desc("Cursor shown over a capturable structure when no capture unit is free, or when one is",
			"already on its way.")]
		public readonly string BlockedCursor = "deploy-blocked";

		public override object Create(ActorInitializer init) { return new CaptureDispatchManager(this); }
	}

	public class CaptureDispatchManager
	{
		public readonly CaptureDispatchManagerInfo Info;

		public CaptureDispatchManager(CaptureDispatchManagerInfo info)
		{
			Info = info;
		}

		/// <summary>
		/// <para>Capture units the local player owns that could take <paramref name="target"/>.</para>
		///
		/// <para>CaptureToNeutral is the filter that separates a technician from a rifleman. Both carry a
		/// Captures trait and both are valid against an enemy-held building, but the rifleman's sets
		/// CaptureToNeutral, so walking it in NEUTRALISES the building rather than taking it. Sending
		/// one here would answer "get me that oil derrick" by giving it to nobody. Selecting on the
		/// flag rather than on the actor name keeps that true if the mod ever adds a second capture
		/// unit.</para>
		/// </summary>
		static IEnumerable<TraitPair<Captures>> EligibleCapturers(World world, Actor target)
		{
			var targetManager = target.TraitOrDefault<CaptureManager>();
			if (targetManager == null)
				return Enumerable.Empty<TraitPair<Captures>>();

			return world.ActorsWithTrait<Captures>()
				.Where(p => p.Actor.Owner == world.LocalPlayer
					&& !p.Actor.IsDead
					&& p.Actor.IsInWorld
					&& !p.Trait.IsTraitDisabled
					&& !p.Trait.Info.CaptureToNeutral
					&& p.Trait.CaptureManager.CanTarget(targetManager));
		}

		/// <summary>
		/// <para>The structure a capture unit already has an order for, or 0 when it is free.</para>
		///
		/// <para>Read off the activity queue rather than off CaptureManager, because CaptureManager does not
		/// learn about a capture until the unit ARRIVES — StartCapture is called from
		/// CaptureActor.TryStartEnter, not when the order resolves. A unit that is still walking is
		/// invisible to the reservation bookkeeping, and walking is precisely the window in which this
		/// feature would otherwise steal it. ActivitiesImplementing walks queued and child activities
		/// too, so a capture sitting behind a move is still seen.</para>
		/// </summary>
		public static uint CommittedTarget(Actor capturer)
		{
			var current = capturer.CurrentActivity;
			if (current == null)
				return 0;

			foreach (var capture in current.ActivitiesImplementing<CaptureActor>())
			{
				var ordered = capture.OrderedTarget;
				if (ordered != null && !ordered.IsDead)
					return ordered.ActorID;
			}

			return 0;
		}

		/// <summary>
		/// Capture units eligible against a FROZEN target — one the player remembers but cannot currently
		/// see. CaptureManager.CanTarget has its own frozen overload reading the remembered owner and the
		/// actor type's Capturable types, which is the same information the stock capture targeter acts
		/// on, so a dispatch is never better informed than an ordinary right-click would have been.
		/// </summary>
		static IEnumerable<TraitPair<Captures>> EligibleCapturers(World world, FrozenActor target)
		{
			return world.ActorsWithTrait<Captures>()
				.Where(p => p.Actor.Owner == world.LocalPlayer
					&& !p.Actor.IsDead
					&& p.Actor.IsInWorld
					&& !p.Trait.IsTraitDisabled
					&& !p.Trait.Info.CaptureToNeutral
					&& p.Trait.CaptureManager.CanTarget(target));
		}

		/// <summary>Decide what a click on <paramref name="target"/> should do, and pick the unit.</summary>
		public CaptureDispatchState Evaluate(World world, Actor target, out Actor capturer)
		{
			capturer = null;

			if (world.LocalPlayer == null || target == null || target.IsDead || !target.IsInWorld)
				return CaptureDispatchState.NotATarget;

			return EvaluateCore(EligibleCapturers(world, target).ToList(), target.ActorID, target.CenterPosition, out capturer);
		}

		/// <summary>
		/// <see cref="Evaluate(World, Actor, out Actor)"/> for a target under fog. The backing actor
		/// supplies the ID the commitment bookkeeping is keyed on, so a technician already walking at a
		/// building is recognised whether the player can currently see it or not.
		/// </summary>
		public CaptureDispatchState Evaluate(World world, FrozenActor target, out Actor capturer)
		{
			capturer = null;

			if (world.LocalPlayer == null || target == null || target.BackingActor == null
				|| target.BackingActor.IsDead || !target.BackingActor.IsInWorld)
				return CaptureDispatchState.NotATarget;

			return EvaluateCore(
				EligibleCapturers(world, target).ToList(), target.BackingActor.ActorID, target.CenterPosition, out capturer);
		}

		/// <summary>
		/// The shared decision, so the live and frozen paths cannot drift on which unit gets picked or on
		/// when a click counts as already covered.
		/// </summary>
		static CaptureDispatchState EvaluateCore(
			List<TraitPair<Captures>> eligible, uint targetId, WPos targetPosition, out Actor capturer)
		{
			capturer = null;

			if (eligible.Count == 0)
				return CaptureDispatchState.NotATarget;

			var committed = eligible.Select(p => CommittedTarget(p.Actor)).ToList();
			if (CaptureDispatchMath.IsAlreadyCovered(committed, targetId))
				return CaptureDispatchState.AlreadyCovered;

			var free = eligible
				.Where((p, i) => CaptureDispatchMath.IsAvailableFor(committed[i], targetId))
				.ToList();

			if (free.Count == 0)
				return CaptureDispatchState.AllCapturersBusy;

			// Nearest free unit. ActorID breaks distance ties so a click is reproducible.
			capturer = free
				.OrderBy(p => (targetPosition - p.Actor.CenterPosition).LengthSquared)
				.ThenBy(p => p.Actor.ActorID)
				.First()
				.Actor;

			return CaptureDispatchState.Available;
		}

		public string CursorForState(CaptureDispatchState state)
		{
			switch (state)
			{
				case CaptureDispatchState.Available:
					return Info.Cursor;
				case CaptureDispatchState.AllCapturersBusy:
				case CaptureDispatchState.AlreadyCovered:
					return Info.BlockedCursor;
				default:
					return null;
			}
		}

		/// <summary>Send the nearest free capture unit at one structure.</summary>
		public IEnumerable<Order> DispatchAt(World world, Actor target, bool queued)
		{
			if (Evaluate(world, target, out var capturer) != CaptureDispatchState.Available)
				yield break;

			yield return new Order("CaptureActor", capturer, Target.FromActor(target), queued);
		}

		/// <summary>Send the nearest free capture unit at a structure the player can only remember.</summary>
		public IEnumerable<Order> DispatchAt(World world, FrozenActor target, bool queued)
		{
			if (Evaluate(world, target, out var capturer) != CaptureDispatchState.Available)
				yield break;

			yield return new Order("CaptureActor", capturer, Target.FromFrozenActor(target), queued);
		}

		/// <summary>
		/// Spread every free capture unit across <paramref name="targets"/> so the LAST structure is
		/// taken as early as possible. Structures somebody is already walking at are dropped first,
		/// so this composes with dispatches the player already made instead of doubling up on them.
		/// </summary>
		public IEnumerable<Order> DispatchAcross(World world, IEnumerable<Actor> targets, bool queued)
		{
			if (world.LocalPlayer == null)
				yield break;

			var structures = targets
				.Where(t => t != null && !t.IsDead && t.IsInWorld && t.TraitOrDefault<CaptureManager>() != null)
				.OrderBy(t => t.ActorID)
				.ToList();

			if (structures.Count == 0)
				yield break;

			// One pool for the whole batch: a unit eligible for any structure in the selection.
			var pool = structures
				.SelectMany(t => EligibleCapturers(world, t))
				.GroupBy(p => p.Actor.ActorID)
				.Select(g => g.First().Actor)
				.OrderBy(a => a.ActorID)
				.ToList();

			var committed = pool.Select(CommittedTarget).ToList();

			var remaining = structures
				.Where(t => !CaptureDispatchMath.IsAlreadyCovered(committed, t.ActorID))
				.ToList();

			var free = pool.Where((a, i) => committed[i] == 0).ToList();

			if (free.Count == 0 || remaining.Count == 0)
				yield break;

			// A capture unit that cannot legally take a given structure must never be assigned to it,
			// so mark those pairs infeasible rather than letting distance decide.
			var cost = CaptureDispatchMath.CostMatrix(
				free.Select(a => a.CenterPosition).ToList(),
				remaining.Select(t => t.CenterPosition).ToList());

			for (var i = 0; i < free.Count; i++)
			{
				var captures = free[i].TraitsImplementing<Captures>()
					.Where(c => !c.IsTraitDisabled && !c.Info.CaptureToNeutral)
					.ToList();

				for (var j = 0; j < remaining.Count; j++)
				{
					var targetManager = remaining[j].TraitOrDefault<CaptureManager>();
					if (targetManager == null || !captures.Any(c => c.CaptureManager.CanTarget(targetManager)))
						cost[i, j] = CaptureDispatchMath.Infeasible;
				}
			}

			var assignment = CaptureDispatchMath.Assign(cost);

			for (var i = 0; i < assignment.Length; i++)
			{
				if (assignment[i] == CaptureDispatchMath.Unassigned)
					continue;

				yield return new Order("CaptureActor", free[i], Target.FromActor(remaining[assignment[i]]), queued);
			}
		}
	}
}
