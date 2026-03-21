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
	public class ResupplyBehaviorSelectorLogic : ChromeLogic
	{
		readonly World world;

		int selectionHash;
		TraitPair<AutoTarget>[] actorStances = Array.Empty<TraitPair<AutoTarget>>();

		[ObjectCreator.UseCtor]
		public ResupplyBehaviorSelectorLogic(Widget widget, World world)
		{
			this.world = world;

			var holdButton = widget.GetOrNull<ButtonWidget>("RESUPPLY_HOLD");
			if (holdButton != null)
				BindResupplyButton(holdButton, ResupplyBehavior.Hold);

			var seekButton = widget.GetOrNull<ButtonWidget>("RESUPPLY_SEEK");
			if (seekButton != null)
				BindResupplyButton(seekButton, ResupplyBehavior.Seek);

			var rotateButton = widget.GetOrNull<ButtonWidget>("RESUPPLY_ROTATE");
			if (rotateButton != null)
				BindResupplyButton(rotateButton, ResupplyBehavior.Rotate);
		}

		void BindResupplyButton(ButtonWidget button, ResupplyBehavior behavior)
		{
			WidgetUtils.BindButtonIcon(button);

			button.IsDisabled = () => { UpdateStateIfNecessary(); return actorStances.Length == 0; };
			button.IsHighlighted = () => actorStances.Any(
				at => !at.Trait.IsTraitDisabled && at.Trait.PredictedResupplyBehavior == behavior);
			button.OnClick = () =>
			{
				var mods = Game.GetModifierKeys();
				if (mods.HasModifier(Modifiers.Alt))
					SetTypeDefault(behavior);
				else if (mods.HasModifier(Modifiers.Ctrl))
					SetUnitDefault(behavior);
				else
					SetSelectionResupplyBehavior(behavior);
			};
		}

		void UpdateStateIfNecessary()
		{
			if (selectionHash == world.Selection.Hash)
				return;

			// Only show for units that have ammo pools
			actorStances = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld
					&& a.TraitsImplementing<AmmoPool>().Any())
				.SelectMany(a => a.TraitsImplementing<AutoTarget>()
					.Where(at => at.Info.EnableStances)
					.Select(at => new TraitPair<AutoTarget>(a, at)))
				.ToArray();

			selectionHash = world.Selection.Hash;
		}

		void SetSelectionResupplyBehavior(ResupplyBehavior behavior)
		{
			foreach (var at in actorStances)
			{
				if (!at.Trait.IsTraitDisabled)
					at.Trait.PredictedResupplyBehavior = behavior;

				world.IssueOrder(new Order("SetResupplyBehavior", at.Actor, false) { ExtraData = (uint)behavior });
			}
		}

		void SetUnitDefault(ResupplyBehavior behavior)
		{
			SetSelectionResupplyBehavior(behavior);
		}

		void SetTypeDefault(ResupplyBehavior behavior)
		{
			var mgr = world.WorldActor.TraitOrDefault<UnitDefaultsManager>();
			if (mgr != null)
			{
				foreach (var actorType in actorStances.Select(at => at.Actor.Info.Name).Distinct())
					mgr.SetResupply(actorType, behavior);
			}

			SetSelectionResupplyBehavior(behavior);
		}
	}
}
