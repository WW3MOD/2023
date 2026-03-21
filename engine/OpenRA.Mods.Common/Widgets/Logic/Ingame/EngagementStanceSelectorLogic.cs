#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
	public class EngagementStanceSelectorLogic : ChromeLogic
	{
		readonly World world;

		int selectionHash;
		TraitPair<AutoTarget>[] actorStances = Array.Empty<TraitPair<AutoTarget>>();

		[ObjectCreator.UseCtor]
		public EngagementStanceSelectorLogic(Widget widget, World world)
		{
			this.world = world;

			var huntButton = widget.GetOrNull<ButtonWidget>("ENGAGEMENT_HUNT");
			if (huntButton != null)
				BindEngagementStanceButton(huntButton, EngagementStance.Hunt);

			var balancedButton = widget.GetOrNull<ButtonWidget>("ENGAGEMENT_BALANCED");
			if (balancedButton != null)
				BindEngagementStanceButton(balancedButton, EngagementStance.Balanced);

			var defensiveButton = widget.GetOrNull<ButtonWidget>("ENGAGEMENT_DEFENSIVE");
			if (defensiveButton != null)
				BindEngagementStanceButton(defensiveButton, EngagementStance.Defensive);

			var holdPositionButton = widget.GetOrNull<ButtonWidget>("ENGAGEMENT_HOLDPOSITION");
			if (holdPositionButton != null)
				BindEngagementStanceButton(holdPositionButton, EngagementStance.HoldPosition);
		}

		void BindEngagementStanceButton(ButtonWidget button, EngagementStance stance)
		{
			WidgetUtils.BindButtonIcon(button);

			button.IsDisabled = () => { UpdateStateIfNecessary(); return actorStances.Length == 0; };
			button.IsHighlighted = () => actorStances.Any(
				at => !at.Trait.IsTraitDisabled && at.Trait.PredictedEngagementStance == stance);
			button.OnClick = () => SetSelectionEngagementStance(stance);
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

			selectionHash = world.Selection.Hash;
		}

		void SetSelectionEngagementStance(EngagementStance stance)
		{
			foreach (var at in actorStances)
			{
				if (!at.Trait.IsTraitDisabled)
					at.Trait.PredictedEngagementStance = stance;

				world.IssueOrder(new Order("SetEngagementStance", at.Actor, false) { ExtraData = (uint)stance });
			}
		}
	}
}
