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

using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Activities
{
	public class VoxelHarvesterDockSequence : HarvesterDockSequence
	{
		readonly IDockClientBody body;
		readonly WithDockingOverlay spriteOverlay;
		bool isDocked;

		public VoxelHarvesterDockSequence(Actor self, Actor refinery, WAngle dockAngle, bool isDragRequired, in WVec dragOffset, int dragLength)
			: base(self, refinery, dockAngle, isDragRequired, dragOffset, dragLength)
		{
			body = self.Trait<IDockClientBody>();
			spriteOverlay = refinery.TraitOrDefault<WithDockingOverlay>();
		}

		public override void OnStateDock(Actor self)
		{
			isDocked = true;
			body.PlayDockAnimation(self, () => { });
			foreach (var trait in self.TraitsImplementing<INotifyHarvesterAction>())
				trait.Docked();
			foreach (var nd in Refinery.TraitsImplementing<INotifyDocking>())
				nd.Docked(Refinery, self);

			if (spriteOverlay != null && !spriteOverlay.Visible)
			{
				spriteOverlay.Visible = true;
				spriteOverlay.WithOffset.Animation.PlayThen(spriteOverlay.Info.Sequence, () =>
				{
					dockingState = DockingState.Loop;
					spriteOverlay.Visible = false;
				});
			}
			else
				dockingState = DockingState.Loop;
		}

		public override void OnStateUndock(Actor self)
		{
			// If we didn't actually dock, skip the undock overlay
			if (!isDocked)
				dockingState = DockingState.Complete;
			else if (Refinery.IsInWorld && !Refinery.IsDead && spriteOverlay != null && !spriteOverlay.Visible)
			{
				dockingState = DockingState.Wait;
				spriteOverlay.Visible = true;
				spriteOverlay.WithOffset.Animation.PlayBackwardsThen(spriteOverlay.Info.Sequence, () =>
				{
					dockingState = DockingState.Complete;
					isDocked = false;
					body.PlayReverseDockAnimation(self, () => { });
					spriteOverlay.Visible = false;

					foreach (var trait in self.TraitsImplementing<INotifyHarvesterAction>())
						trait.Undocked();

					if (Refinery.IsInWorld && !Refinery.IsDead)
						foreach (var nd in Refinery.TraitsImplementing<INotifyDocking>())
							nd.Undocked(Refinery, self);
				});
			}
			else
			{
				dockingState = DockingState.Complete;
				isDocked = false;
				body.PlayReverseDockAnimation(self, () => { });

				foreach (var trait in self.TraitsImplementing<INotifyHarvesterAction>())
					trait.Undocked();

				if (Refinery.IsInWorld && !Refinery.IsDead)
					foreach (var nd in Refinery.TraitsImplementing<INotifyDocking>())
						nd.Undocked(Refinery, self);
			}
		}
	}
}
