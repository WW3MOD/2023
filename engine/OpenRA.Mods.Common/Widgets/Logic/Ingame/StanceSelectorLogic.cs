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

using System;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class StanceSelectorLogic : ChromeLogic
	{
		readonly World world;

		int selectionHash;
		TraitPair<AutoTarget>[] actorStances = Array.Empty<TraitPair<AutoTarget>>();

		[ObjectCreator.UseCtor]
		public StanceSelectorLogic(Widget widget, World world)
		{
			this.world = world;

			var holdFireButton = widget.GetOrNull<ButtonWidget>("STANCE_HOLDFIRE");
			if (holdFireButton != null)
				BindStanceButton(holdFireButton, UnitStance.HoldFire);

			var ambushButton = widget.GetOrNull<ButtonWidget>("STANCE_AMBUSH");
			if (ambushButton != null)
				BindStanceButton(ambushButton, UnitStance.Ambush);

			var fireAtWillButton = widget.GetOrNull<ButtonWidget>("STANCE_FIREATWILL");
			if (fireAtWillButton != null)
				BindStanceButton(fireAtWillButton, UnitStance.FireAtWill);
		}

		void BindStanceButton(ButtonWidget button, UnitStance stance)
		{
			WidgetUtils.BindButtonIcon(button);

			button.IsDisabled = () => { UpdateStateIfNecessary(); return actorStances.Length == 0; };
			button.IsHighlighted = () => actorStances.Any(
				at => !at.Trait.IsTraitDisabled && at.Trait.PredictedStance == stance);
			button.OnClick = () =>
			{
				var mods = Game.GetModifierKeys();
				if (mods.HasModifier(Modifiers.Ctrl) && mods.HasModifier(Modifiers.Alt))
					SetTypeDefault(stance);
				else if (mods.HasModifier(Modifiers.Alt))
					DoNow(stance);
				else if (mods.HasModifier(Modifiers.Ctrl))
					SetUnitDefault(stance);
				else
					SetSelectionStance(stance);
			};
		}

		void UpdateStateIfNecessary()
		{
			if (selectionHash == world.Selection.Hash)
				return;

			actorStances = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld)
				.SelectMany(a => a.TraitsImplementing<AutoTarget>()
					.Where(at => at.Info.EnableStances)
					.Select(at => new TraitPair<AutoTarget>(a, at)))
				.ToArray();

			// DEBUG: Diagnose why garrison buildings may not show stances
			if (actorStances.Length == 0 && world.Selection.Actors.Any())
			{
				foreach (var a in world.Selection.Actors)
				{
					var hasAT = a.TraitsImplementing<AutoTarget>().Any();
					var hasATEnabled = a.TraitsImplementing<AutoTarget>().Any(at => at.Info.EnableStances);
					var isLocal = a.Owner == world.LocalPlayer;
					var inWorld = a.IsInWorld;
					if (a.Info.HasTraitInfo<CargoInfo>()) // Only log for potential garrison buildings
						TextNotificationsManager.Debug("[STANCE DEBUG] {0}: Owner={1} LocalPlayer={2} IsLocal={3} InWorld={4} HasAutoTarget={5} HasATEnabled={6}",
							a.Info.Name, a.Owner.PlayerName, world.LocalPlayer?.PlayerName ?? "null", isLocal, inWorld, hasAT, hasATEnabled);
				}
			}

			selectionHash = world.Selection.Hash;
		}

		void SetSelectionStance(UnitStance stance)
		{
			foreach (var at in actorStances)
			{
				if (!at.Trait.IsTraitDisabled)
					at.Trait.PredictedStance = stance;

				world.IssueOrder(new Order("SetUnitStance", at.Actor, false) { ExtraData = (uint)stance });
			}
		}

		void SetUnitDefault(UnitStance stance)
		{
			SetSelectionStance(stance);
		}

		void SetTypeDefault(UnitStance stance)
		{
			var mgr = world.WorldActor.TraitOrDefault<UnitDefaultsManager>();
			if (mgr != null)
			{
				foreach (var actorType in actorStances.Select(at => at.Actor.Info.Name).Distinct())
					mgr.SetFireStance(actorType, stance);
			}

			SetSelectionStance(stance);
		}

		void DoNow(UnitStance stance)
		{
			SetSelectionStance(stance);

			// Hard stop: cancel all current orders
			foreach (var at in actorStances)
				world.IssueOrder(new Order("Stop", at.Actor, false));
		}
	}
}
