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
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Effects;
using OpenRA.Primitives;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class ShellmapNukeOverlayWidget : Widget
	{
		readonly World world;
		readonly WorldRenderer worldRenderer;
		WeaponInfo atomicWeapon;
		bool isActive;

		public bool IsNukeMode => isActive;

		Action onDeactivate;

		[ObjectCreator.UseCtor]
		public ShellmapNukeOverlayWidget(World world, WorldRenderer worldRenderer)
		{
			this.world = world;
			this.worldRenderer = worldRenderer;

			// Try to resolve the atomic weapon from rules
			world.Map.Rules.Weapons.TryGetValue("atomic", out atomicWeapon);
		}

		public void Activate(Action onDeactivateCallback)
		{
			isActive = true;
			onDeactivate = onDeactivateCallback;
		}

		public void Deactivate()
		{
			isActive = false;
			onDeactivate?.Invoke();
			onDeactivate = null;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (!isActive)
				return false;

			if (mi.Event != MouseInputEvent.Down)
				return true;

			// Right-click or Escape cancels
			if (mi.Button == MouseButton.Right)
			{
				Deactivate();
				return true;
			}

			if (mi.Button == MouseButton.Left)
			{
				FireNuke(mi.Location);
				Deactivate();
				return true;
			}

			return true;
		}

		public override bool HandleKeyPress(KeyInput e)
		{
			if (!isActive)
				return false;

			if (e.Event == KeyInputEvent.Down && e.Key == Keycode.ESCAPE)
			{
				Deactivate();
				return true;
			}

			return false;
		}

		void FireNuke(int2 screenPos)
		{
			if (atomicWeapon == null)
				return;

			var targetCell = worldRenderer.Viewport.ViewToWorld(screenPos);
			var targetPos = world.Map.CenterOfCell(targetCell);

			// Use NATO player as the firer (first non-neutral player)
			var firedBy = world.Players.FirstOrDefault(p => p.InternalName == "NATO")
				?? world.Players.FirstOrDefault(p => !p.NonCombatant && p.InternalName != "Neutral");

			if (firedBy == null)
				return;

			var missile = new NukeLaunch(
				firedBy,
				"atomic",           // MissileImage
				atomicWeapon,
				"effect",           // palette
				"up",               // upSequence
				"down",             // downSequence
				WPos.Zero,          // launchPos (unused with skipAscent)
				targetPos,
				new WDist(6400),    // DetonationAltitude: 6c256
				true,               // removeOnDetonation
				new WDist(1024),    // FlightVelocity
				5,                  // MissileDelay
				70,                 // FlightDelay (impactDelay)
				true,               // skipAscent — no MSLO building, drop from sky
				null,               // trailImage
				Array.Empty<string>(),
				"effect",
				false,
				1,
				1);

			world.AddFrameEndTask(w => w.Add(missile));
		}

		public override string GetCursor(int2 pos)
		{
			return isActive ? "nuke" : null;
		}
	}
}
