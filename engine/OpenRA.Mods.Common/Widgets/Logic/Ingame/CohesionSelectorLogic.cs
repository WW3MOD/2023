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
	public class CohesionSelectorLogic : ChromeLogic
	{
		readonly World world;

		int selectionHash;
		TraitPair<AutoTarget>[] actorStances = Array.Empty<TraitPair<AutoTarget>>();

		[ObjectCreator.UseCtor]
		public CohesionSelectorLogic(Widget widget, World world)
		{
			this.world = world;

			var tightButton = widget.GetOrNull<ButtonWidget>("COHESION_TIGHT");
			if (tightButton != null)
				BindCohesionButton(tightButton, CohesionMode.Tight);

			var looseButton = widget.GetOrNull<ButtonWidget>("COHESION_LOOSE");
			if (looseButton != null)
				BindCohesionButton(looseButton, CohesionMode.Loose);

			var spreadButton = widget.GetOrNull<ButtonWidget>("COHESION_SPREAD");
			if (spreadButton != null)
				BindCohesionButton(spreadButton, CohesionMode.Spread);
		}

		void BindCohesionButton(ButtonWidget button, CohesionMode mode)
		{
			WidgetUtils.BindButtonIcon(button);

			button.IsDisabled = () => { UpdateStateIfNecessary(); return actorStances.Length == 0; };
			button.IsHighlighted = () => actorStances.Any(
				at => !at.Trait.IsTraitDisabled && at.Trait.PredictedCohesion == mode);
			button.OnClick = () =>
			{
				var mods = Game.GetModifierKeys();
				if (mods.HasModifier(Modifiers.Alt))
					SetTypeDefault(mode);
				else if (mods.HasModifier(Modifiers.Ctrl))
					SetUnitDefault(mode);
				else
					SetSelectionCohesion(mode);
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

			selectionHash = world.Selection.Hash;
		}

		void SetSelectionCohesion(CohesionMode mode)
		{
			foreach (var at in actorStances)
			{
				if (!at.Trait.IsTraitDisabled)
					at.Trait.PredictedCohesion = mode;

				world.IssueOrder(new Order("SetCohesion", at.Actor, false) { ExtraData = (uint)mode });
			}
		}

		void SetUnitDefault(CohesionMode mode)
		{
			SetSelectionCohesion(mode);
		}

		void SetTypeDefault(CohesionMode mode)
		{
			var mgr = world.WorldActor.TraitOrDefault<UnitDefaultsManager>();
			if (mgr != null)
			{
				foreach (var actorType in actorStances.Select(at => at.Actor.Info.Name).Distinct())
					mgr.SetCohesion(actorType, mode);
			}

			SetSelectionCohesion(mode);
		}
	}
}
