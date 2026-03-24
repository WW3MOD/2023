<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
=======
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

>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>Contains all functions that are unit-specific.</summary>
	public class CommandBarLogic : ChromeLogic
	{
		readonly World world;

		int selectionHash;
		Actor[] selectedActors = Array.Empty<Actor>();
		bool attackMoveDisabled = true;
		bool forceMoveDisabled = true;
		bool forceAttackDisabled = true;
		bool guardDisabled = true;
		bool scatterDisabled = true;
		bool resupplyDisabled = true;
		bool stopDisabled = true;
		bool waypointModeDisabled = true;
		bool patrolDisabled = true;


		int deployHighlighted;
		int scatterHighlighted;
		int resupplyHighlighted;
		int stopHighlighted;
		int patrolHighlighted;


		TraitPair<IIssueDeployOrder>[] selectedDeploys = Array.Empty<TraitPair<IIssueDeployOrder>>();

		[ObjectCreator.UseCtor]
		public CommandBarLogic(Widget widget, World world, Dictionary<string, MiniYaml> logicArgs)
		{
			this.world = world;

			var highlightOnButtonPress = false;
			if (logicArgs.TryGetValue("HighlightOnButtonPress", out var entry))
				highlightOnButtonPress = FieldLoader.GetValue<bool>("HighlightOnButtonPress", entry.Value);

			var attackMoveButton = widget.GetOrNull<ButtonWidget>("ATTACK_MOVE");
			if (attackMoveButton != null)
			{
				WidgetUtils.BindButtonIcon(attackMoveButton);

				attackMoveButton.IsDisabled = () => { UpdateStateIfNecessary(); return attackMoveDisabled; };
				attackMoveButton.IsHighlighted = () => world.OrderGenerator is AttackMoveOrderGenerator;

				void Toggle(bool allowCancel)
				{
					if (attackMoveButton.IsHighlighted() && allowCancel)
						world.CancelInputMode();
					else
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
						world.OrderGenerator = new AttackMoveOrderGenerator(selectedActors);
				};
=======
						world.OrderGenerator = new AttackMoveOrderGenerator(selectedActors, Game.Settings.Game.MouseButtonPreference.Action);
				}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

				attackMoveButton.OnClick = () => Toggle(true);
				attackMoveButton.OnKeyPress = _ => Toggle(false);
			}

			var forceMoveButton = widget.GetOrNull<ButtonWidget>("FORCE_MOVE");
			if (forceMoveButton != null)
			{
				WidgetUtils.BindButtonIcon(forceMoveButton);

				forceMoveButton.IsDisabled = () => { UpdateStateIfNecessary(); return forceMoveDisabled; };
				forceMoveButton.IsHighlighted = () =>
					!forceMoveButton.IsDisabled() && IsForceModifiersActive(Game.Settings.Game.ForceMoveModifiers, Game.Settings.Game.ForceAttackModifiers) && !(world.OrderGenerator is AttackMoveOrderGenerator);
				forceMoveButton.OnClick = () =>
				{
					if (forceMoveButton.IsHighlighted())
						world.CancelInputMode();
					else
						world.OrderGenerator = new ForceModifiersOrderGenerator(Game.Settings.Game.ForceMoveModifiers, true);
				};
			}

			var forceAttackButton = widget.GetOrNull<ButtonWidget>("FORCE_ATTACK");
			if (forceAttackButton != null)
			{
				WidgetUtils.BindButtonIcon(forceAttackButton);

				forceAttackButton.IsDisabled = () => { UpdateStateIfNecessary(); return forceAttackDisabled; };
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				forceAttackButton.IsHighlighted = () =>
					!forceAttackButton.IsDisabled() && IsForceModifiersActive(Game.Settings.Game.ForceAttackModifiers, Modifiers.None);
=======
				forceAttackButton.IsHighlighted = () => !forceAttackButton.IsDisabled() && IsForceModifiersActive(Modifiers.Ctrl)
					&& world.OrderGenerator is not AttackMoveOrderGenerator;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

				forceAttackButton.OnClick = () =>
				{
					if (forceAttackButton.IsHighlighted())
						world.CancelInputMode();
					else
						world.OrderGenerator = new ForceModifiersOrderGenerator(Game.Settings.Game.ForceAttackModifiers, true);
				};
			}

			var guardButton = widget.GetOrNull<ButtonWidget>("GUARD");
			if (guardButton != null)
			{
				WidgetUtils.BindButtonIcon(guardButton);

				guardButton.IsDisabled = () => { UpdateStateIfNecessary(); return guardDisabled; };
				guardButton.IsHighlighted = () => world.OrderGenerator is GuardOrderGenerator;

				void Toggle(bool allowCancel)
				{
					if (guardButton.IsHighlighted())
					{
						if (allowCancel)
							world.CancelInputMode();
					}
					else
						world.OrderGenerator = new GuardOrderGenerator(selectedActors,
							"Guard", "guard", Game.Settings.Game.MouseButtonPreference.Action);
				}

				guardButton.OnClick = () => Toggle(true);
				guardButton.OnKeyPress = _ => Toggle(false);
			}

			var scatterButton = widget.GetOrNull<ButtonWidget>("SCATTER");
			if (scatterButton != null)
			{
				WidgetUtils.BindButtonIcon(scatterButton);

				scatterButton.IsDisabled = () => { UpdateStateIfNecessary(); return scatterDisabled; };
				scatterButton.IsHighlighted = () => scatterHighlighted > 0;
				scatterButton.OnClick = () =>
				{
					if (highlightOnButtonPress)
						scatterHighlighted = 2;

					var queued = Game.GetModifierKeys().HasModifier(Modifiers.Shift);
					PerformKeyboardOrderOnSelection(a => new Order("Scatter", a, queued));
				};

				scatterButton.OnKeyPress = ki => { scatterHighlighted = 2; scatterButton.OnClick(); };
			}

			var deployButton = widget.GetOrNull<ButtonWidget>("DEPLOY");
			if (deployButton != null)
			{
				WidgetUtils.BindButtonIcon(deployButton);

				deployButton.IsDisabled = () =>
				{
					UpdateStateIfNecessary();

					var queued = Game.GetModifierKeys().HasModifier(Modifiers.Shift);
					return !selectedDeploys.Any(pair => pair.Trait.CanIssueDeployOrder(pair.Actor, queued));
				};

				deployButton.IsHighlighted = () => deployHighlighted > 0;
				deployButton.OnClick = () =>
				{
					if (highlightOnButtonPress)
						deployHighlighted = 2;

					var queued = Game.GetModifierKeys().HasModifier(Modifiers.Shift);
					PerformDeployOrderOnSelection(queued);
				};

				deployButton.OnKeyPress = ki => { deployHighlighted = 2; deployButton.OnClick(); };
			}

			var resupplyButton = widget.GetOrNull<ButtonWidget>("RESUPPLY");
			if (resupplyButton != null)
			{
				WidgetUtils.BindButtonIcon(resupplyButton);
				resupplyButton.IsDisabled = () => { UpdateStateIfNecessary(); return resupplyDisabled; };
				resupplyButton.IsHighlighted = () => resupplyHighlighted > 0;
				resupplyButton.OnClick = () =>
				{
					if (highlightOnButtonPress)
						resupplyHighlighted = 2;

					var queued = Game.GetModifierKeys().HasModifier(Modifiers.Shift);
					PerformKeyboardOrderOnSelection(a => new Order("Resupply", a, queued));
				};
				resupplyButton.OnDoubleClick = () =>
				{
					if (highlightOnButtonPress)
						resupplyHighlighted = 2;

					var queued = Game.GetModifierKeys().HasModifier(Modifiers.Shift);
					PerformKeyboardOrderOnSelection(a => new Order("Resupply", a, queued));
				};

				resupplyButton.OnKeyPress = ki => { resupplyHighlighted = 2; resupplyButton.OnClick(); };
			}

			var patrolButton = widget.GetOrNull<ButtonWidget>("PATROL");
			if (patrolButton != null)
			{
				WidgetUtils.BindButtonIcon(patrolButton);
				patrolButton.IsDisabled = () => { UpdateStateIfNecessary(); return patrolDisabled; };
				patrolButton.IsHighlighted = () => world.OrderGenerator is PatrolOrderGenerator;
				patrolButton.OnClick = () =>
				{
					if (world.OrderGenerator is PatrolOrderGenerator pg)
					{
						// Second click confirms the patrol route
						pg.Confirm(world);
					}
					else
					{
						// First click enters patrol waypoint mode
						world.OrderGenerator = new PatrolOrderGenerator(selectedActors);
					}
				};

				patrolButton.OnKeyPress = ki => patrolButton.OnClick();
			}

			var stopButton = widget.GetOrNull<ButtonWidget>("STOP");
			if (stopButton != null)
			{
				WidgetUtils.BindButtonIcon(stopButton);

				stopButton.IsDisabled = () => { UpdateStateIfNecessary(); return stopDisabled; };
				stopButton.IsHighlighted = () => stopHighlighted > 0;
				stopButton.OnClick = () =>
				{
					if (highlightOnButtonPress)
						stopHighlighted = 2;

					PerformKeyboardOrderOnSelection(a => new Order("Stop", a, false));
				};

				stopButton.OnKeyPress = ki => { stopHighlighted = 2; stopButton.OnClick(); };
			}

			var queueOrdersButton = widget.GetOrNull<ButtonWidget>("QUEUE_ORDERS");
			if (queueOrdersButton != null)
			{
				WidgetUtils.BindButtonIcon(queueOrdersButton);

				queueOrdersButton.IsDisabled = () => { UpdateStateIfNecessary(); return waypointModeDisabled; };
				queueOrdersButton.IsHighlighted = () => !queueOrdersButton.IsDisabled() && IsForceModifiersActive(Modifiers.Shift, Modifiers.None);
				queueOrdersButton.OnClick = () =>
				{
					if (queueOrdersButton.IsHighlighted())
						world.CancelInputMode();
					else
						world.OrderGenerator = new ForceModifiersOrderGenerator(Modifiers.Shift, false);
				};
			}

			var keyOverrides = widget.GetOrNull<LogicKeyListenerWidget>("MODIFIER_OVERRIDES");
			if (keyOverrides != null)
			{
				var noShiftButtons = new[] { guardButton, deployButton, scatterButton, attackMoveButton };
				var keyUpButtons = new[] { guardButton, attackMoveButton };
				keyOverrides.AddHandler(e =>
				{
					var currentModifiers = Game.GetModifierKeys();
					// HACK: allow command buttons to be triggered if the shift (queue order modifier) key is held
					if (e.Modifiers.HasModifier(Modifiers.Shift))
					{
						var eNoShift = e;
						eNoShift.Modifiers &= ~Modifiers.Shift;

						foreach (var b in noShiftButtons)
						{
							if (b == null || b.IsDisabled() || !b.Key.IsActivatedBy(eNoShift))
								continue;

							if (!(b.DisableKeyRepeat ^ e.IsRepeat) || (e.Event == KeyInputEvent.Up && !keyUpButtons.Contains(b)))
								continue;

							b.OnKeyPress(e);
							return true;
						}
					}

					// WW3MOD: Assault Move is disabled; only AttackMove is triggered
					if (attackMoveButton != null && !attackMoveDisabled)
					{
						// Prioritize ForceAttack (Ctrl + Alt), strip Shift so queued force-attack works
						if ((currentModifiers & ~Modifiers.Shift) == Game.Settings.Game.ForceAttackModifiers)
						{
							if (e.Event == KeyInputEvent.Down)
							{
								selectionHash = 0; // Force selection update
								UpdateStateIfNecessary();
								world.OrderGenerator = new ForceModifiersOrderGenerator(Game.Settings.Game.ForceAttackModifiers, true);
							}
							else if (e.Event == KeyInputEvent.Up)
							{
								world.CancelInputMode();
							}

							return true;
						}

						// AttackMove requires Alt alone (or with Shift)
						// On KeyUp, Game.GetModifierKeys() already has Alt removed, so we
						// also check if we're currently in AttackMove mode below
						else if (currentModifiers.HasModifier(Game.Settings.Game.AttackMoveModifiers) && !currentModifiers.HasModifier(Game.Settings.Game.ForceMoveModifiers))
						{
							if (e.Event == KeyInputEvent.Down)
							{
								selectionHash = 0; // Force selection update
								UpdateStateIfNecessary();
								world.OrderGenerator = new AttackMoveOrderGenerator(selectedActors);
							}

							return true;
						}
					}

					// Cancel modifier-driven order generators on key release when the required
					// modifiers are no longer held. GetModifierKeys() already reflects the
					// released key, so the condition checks above won't match — handle it here.
					if (e.Event == KeyInputEvent.Up)
					{
						if (world.OrderGenerator is AttackMoveOrderGenerator)
						{
							world.CancelInputMode();
							return true;
						}

						// ForceAttack (Ctrl+Alt) → if either modifier is released, cancel
						if (world.OrderGenerator is ForceModifiersOrderGenerator
							&& (currentModifiers & ~Modifiers.Shift) != Game.Settings.Game.ForceAttackModifiers)
						{
							world.CancelInputMode();
							return true;
						}
					}

					return false;
				});
			}
		}

		public override void Tick()
		{
			if (deployHighlighted > 0)
				deployHighlighted--;

			if (scatterHighlighted > 0)
				scatterHighlighted--;

			if (stopHighlighted > 0)
				stopHighlighted--;

			if (patrolHighlighted > 0)
				patrolHighlighted--;

			base.Tick();
		}

		bool IsForceModifiersActive(Modifiers primaryModifier, Modifiers conflictingModifier)
		{
			if (world.OrderGenerator is ForceModifiersOrderGenerator fmog && fmog.Modifiers == primaryModifier)
				return true;

			var currentModifiers = Game.GetModifierKeys();
			var result = (world.OrderGenerator is UnitOrderGenerator || world.OrderGenerator is null)
				&& currentModifiers == primaryModifier
				&& (conflictingModifier == Modifiers.None || !currentModifiers.HasFlag(conflictingModifier));
			return result;
		}

		void UpdateStateIfNecessary()
		{
			if (selectionHash == world.Selection.Hash)
				return;

			selectedActors = world.Selection.Actors
				.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead)
				.ToArray();

			attackMoveDisabled = !selectedActors.Any(a => a.Info.HasTraitInfo<AttackMoveInfo>() && a.Info.HasTraitInfo<AutoTargetInfo>());
			guardDisabled = !selectedActors.Any(a => a.Info.HasTraitInfo<GuardInfo>() && a.Info.HasTraitInfo<AutoTargetInfo>());
			forceMoveDisabled = !selectedActors.Any(a => a.Info.HasTraitInfo<MobileInfo>() || a.Info.HasTraitInfo<AircraftInfo>());
			forceAttackDisabled = !selectedActors.Any(a => a.Info.HasTraitInfo<AttackBaseInfo>());
			scatterDisabled = !selectedActors.Any(a => a.Info.HasTraitInfo<IMoveInfo>());
			resupplyDisabled = !selectedActors.Any(a => a.Info.HasTraitInfo<AmmoPoolInfo>());
			patrolDisabled = !selectedActors.Any(a => a.Info.HasTraitInfo<IMoveInfo>());

			selectedDeploys = selectedActors
				.SelectMany(a => a.TraitsImplementing<IIssueDeployOrder>()
					.Select(d => new TraitPair<IIssueDeployOrder>(a, d)))
				.ToArray();

			var cbbInfos = selectedActors.Select(a => a.Info.TraitInfoOrDefault<CommandBarBlacklistInfo>()).ToArray();
			stopDisabled = !cbbInfos.Any(i => i == null || !i.DisableStop);
			waypointModeDisabled = !cbbInfos.Any(i => i == null || !i.DisableWaypointMode);

			selectionHash = world.Selection.Hash;
		}

		void PerformKeyboardOrderOnSelection(Func<Actor, Order> f)
		{
			UpdateStateIfNecessary();

			var orders = selectedActors
				.Select(f)
				.ToArray();

			foreach (var o in orders)
				world.IssueOrder(o);

			orders.PlayVoiceForOrders();
		}

		void PerformDeployOrderOnSelection(bool queued)
		{
			UpdateStateIfNecessary();

			var undeployed = selectedDeploys
				.Where(pair => pair.Actor.UnDeployed());

			var unitsToIssueOrderTo = undeployed.Any() ? undeployed : selectedDeploys;

			var orders = unitsToIssueOrderTo
				.Where(pair => pair.Trait.CanIssueDeployOrder(pair.Actor, queued))
				.Select(d => d.Trait.IssueDeployOrder(d.Actor, queued))
				.Where(d => d != null)
				.ToArray();

			foreach (var o in orders)
				world.IssueOrder(o);

			orders.PlayVoiceForOrders();
		}
	}
}
