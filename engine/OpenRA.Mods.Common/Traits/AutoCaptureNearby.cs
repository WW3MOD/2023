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
	[Desc("When this unit goes idle, walk to a nearby capturable structure and take it, without being",
		"told to. Neutral and enemy-held structures alike — the eligibility is whatever the unit's own",
		"Captures trait already permits, so this grants no capture the player could not have ordered by",
		"hand.",
		"TWO STANCE AXES, because AutoTarget has two and they answer different questions. The FIRE",
		"stance gates the behaviour: HoldFire turns it off for that unit and is the per-unit off switch,",
		"deliberately an existing control rather than a new one. The ENGAGEMENT stance sizes it:",
		"HoldPosition never ventures at all, Defensive uses the modest DefensiveRadiusCells, Hunt uses",
		"the longer HuntRadiusCells. A fresh unit is FireAtWill + Defensive (AutoTarget.cs:75,167), so",
		"the behaviour is ON by default at the conservative radius, which is what was asked for.",
		"Idle-triggered, so it never interrupts an order. A technician walking somewhere under a player",
		"order is not idle and is not touched.")]
	public class AutoCaptureNearbyInfo : TraitInfo, Requires<IMoveInfo>, Requires<CaptureManagerInfo>
	{
		[Desc("Master switch. Ships ON: the user's requirement is that an untouched technician captures",
			"nearby structures by itself and that turning it OFF is the deliberate act. The per-unit",
			"off switch is the HoldFire stance; this one is the mod-wide one.")]
		public readonly bool Enabled = true;

		[Desc("Radius in cells on the Defensive engagement stance — the shipped default stance. Kept",
			"short on purpose: the brief was that a technician should take what is next to it, not go",
			"hunting. This is STRAIGHT-LINE distance, not path length, so a derrick across a river is",
			"admitted here and only fails later when the walk cannot reach it.")]
		public readonly int DefensiveRadiusCells = 8;

		[Desc("Radius in cells on the Hunt engagement stance — the player asking for eagerness. Hunt",
			"already means 'go and look for it' everywhere else in the mod, so it is the natural place",
			"to put the longer leash rather than inventing a separate toggle.")]
		public readonly int HuntRadiusCells = 20;

		[Desc("Two structures whose distances differ by less than this many cells count as equally near,",
			"and the more valuable of them wins. 0 degrades to pure nearest-first.",
			"Value is read from CashTrickler.Amount — what the structure actually pays — because tech",
			"buildings carry no Valued trait at all (structures-neutral.yaml). A structure that pays",
			"nothing scores 0 and is taken only when nothing that pays is equally close.")]
		public readonly int ValueTieBreakCells = 3;

		[Desc("Ticks between idle re-scans. INotifyIdle fires every tick an actor stays idle, so this",
			"throttles the structure scan; the phase is staggered per actor deterministically.")]
		public readonly int ScanInterval = 40;

		[Desc("Skip actors owned by a bot. Ships true, and this is NOT a gate withholding an improvement",
			"from @stable — the bots already have capture automation of their own in",
			"CaptureCoordinatorBotModule, which both profiles run with technicians drawn by",
			"UnitRole.CaptureSpecialist (architecture.md). Running this trait on top would put two",
			"schedulers on one consumable unit: the coordinator picks a target on its own cadence, and a",
			"technician idling between its decisions would be walked somewhere else by this trait. That",
			"is a regression for the bot, not an improvement, which is why it is excluded rather than",
			"gated. Set false to let bot technicians use this as well.")]
		public readonly bool SkipBotOwners = true;

		public override object Create(ActorInitializer init) { return new AutoCaptureNearby(init.Self, this); }
	}

	public class AutoCaptureNearby : INotifyCreated, INotifyIdle
	{
		readonly AutoCaptureNearbyInfo info;
		readonly Actor self;

		CaptureManager captureManager;
		AutoTarget autoTarget;
		Captures[] captures;
		int scanTicks;

		public AutoCaptureNearby(Actor self, AutoCaptureNearbyInfo info)
		{
			this.info = info;
			this.self = self;

			// Deterministic per-actor phase so a squad that goes idle together does not scan on the same
			// tick. Must NOT come from World.SharedRandom: this trait ships enabled, so drawing from the
			// synced stream would shift it for control games too (conventions.md).
			scanTicks = info.ScanInterval > 0 ? (int)(self.ActorID % (uint)info.ScanInterval) : 0;
		}

		void INotifyCreated.Created(Actor _)
		{
			captureManager = self.TraitOrDefault<CaptureManager>();
			autoTarget = self.TraitOrDefault<AutoTarget>();

			// The capture traits that TAKE rather than neutralise. A rifleman's Captures sets
			// CaptureToNeutral, so walking it into a building gives it to nobody — auto-capturing with
			// one would spend a soldier to hand the enemy's derrick to Neutral, which no player asked
			// for. Same structural filter CaptureDispatchManager applies, for the same reason.
			captures = self.TraitsImplementing<Captures>()
				.Where(c => !c.Info.CaptureToNeutral)
				.ToArray();
		}

		void INotifyIdle.TickIdle(Actor _)
		{
			if (!info.Enabled || captureManager == null || captures.Length == 0)
				return;

			if (info.SkipBotOwners && self.Owner.IsBot)
				return;

			if (--scanTicks > 0)
				return;

			scanTicks = info.ScanInterval;

			if (self.IsDead || !self.IsInWorld)
				return;

			// The fire stance gates, the engagement stance sizes. A unit without AutoTarget has no
			// stances to consult and is treated as fully permissive at the Defensive radius, which is
			// the same fallback AutoSeekSupplies applies.
			var fireStance = autoTarget?.Stance ?? UnitStance.FireAtWill;
			if (!AutoCaptureMath.StancePermitsAutoCapture(fireStance))
				return;

			var radiusCells = AutoCaptureMath.RadiusCellsForStance(
				autoTarget?.EngagementStanceValue ?? EngagementStance.Defensive,
				info.DefensiveRadiusCells,
				info.HuntRadiusCells);

			if (radiusCells <= 0)
				return;

			var target = FindTarget(radiusCells);
			if (target == null)
				return;

			self.QueueActivity(false, new CaptureActor(self, Target.FromActor(target), captures[0].Info.TargetLineColor));
			self.ShowTargetLines();
		}

		/// <summary>
		/// Nearest capturable structure inside the radius that this unit could legally take and that
		/// nobody is already walking at, with value breaking near-ties.
		/// </summary>
		Actor FindTarget(int radiusCells)
		{
			var candidates = new List<AutoCaptureMath.Candidate>();
			var actors = new List<Actor>();

			// Committed targets are read off every capture unit the player owns, not just this one, so
			// two idle technicians standing together do not both walk at the same derrick and spend two
			// consumable units on one building. This is the same activity-queue reading
			// CaptureDispatchManager uses, and for the same reason: CaptureManager does not learn about
			// a capture until the unit ARRIVES, and walking is the whole window that matters here.
			var committed = new HashSet<uint>();
			foreach (var p in self.World.ActorsWithTrait<Captures>())
			{
				if (p.Actor.Owner != self.Owner || p.Actor == self)
					continue;

				var id = CaptureDispatchManager.CommittedTarget(p.Actor);
				if (id != 0)
					committed.Add(id);
			}

			foreach (var a in self.World.FindActorsInCircle(self.CenterPosition, WDist.FromCells(radiusCells)))
			{
				if (a == self || a.IsDead || !a.IsInWorld || committed.Contains(a.ActorID))
					continue;

				var targetManager = a.TraitOrDefault<CaptureManager>();
				if (targetManager == null || !captureManager.CanTarget(targetManager))
					continue;

				// Hidden actors are deliberately not filtered out: FindActorsInCircle only returns live
				// actors, and this radius is short enough that anything inside it is next to a unit the
				// player owns. A shroud test here would make the behaviour depend on which client is
				// asking, and this runs on the synced side.
				var distance = (a.CenterPosition - self.CenterPosition).Length;

				candidates.Add(new AutoCaptureMath.Candidate(distance, StructureValue(a), a.ActorID));
				actors.Add(a);
			}

			var best = AutoCaptureMath.SelectBest(candidates, info.ValueTieBreakCells * 1024);
			return best == AutoCaptureMath.NoTarget ? null : actors[best];
		}

		/// <summary>
		/// What a structure is worth, for the near-tie break only. CashTrickler.Amount is the honest
		/// answer in this mod: the tech buildings carry no Valued trait, and what makes a derrick worth
		/// walking to is the income it pays. Anything that pays nothing scores 0.
		/// </summary>
		static int StructureValue(Actor structure)
		{
			var total = 0;
			foreach (var t in structure.Info.TraitInfos<CashTricklerInfo>())
				total += t.Amount;

			return total;
		}
	}
}
