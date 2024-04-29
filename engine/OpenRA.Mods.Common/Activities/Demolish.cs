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

using System.Linq;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	class Demolish : Enter
	{
		readonly int delay;
		readonly int flashes;
		readonly int flashesDelay;
		readonly int flashInterval;
		readonly BitSet<DamageType> damageTypes;
		readonly INotifyDemolition[] notifiers;
		readonly EnterBehaviour enterBehaviour;

		Actor enterActor;
		IDemolishable[] enterDemolishables;

		public Demolish(Actor self, in Target target, EnterBehaviour enterBehaviour, int delay, int flashes,
			int flashesDelay, int flashInterval, BitSet<DamageType> damageTypes, Color? targetLineColor)
			: base(self, target, targetLineColor)
		{
			notifiers = self.TraitsImplementing<INotifyDemolition>().ToArray();
			this.delay = delay;
			this.flashes = flashes;
			this.flashesDelay = flashesDelay;
			this.flashInterval = flashInterval;
			this.damageTypes = damageTypes;
			this.enterBehaviour = enterBehaviour;
		}

		protected override bool TryStartEnter(Actor self, Actor targetActor)
		{
			enterActor = targetActor;
			enterDemolishables = targetActor.TraitsImplementing<IDemolishable>().ToArray();

			// Make sure we can still demolish the target before entering
			// (but not before, because this may stop the actor in the middle of nowhere)
			var useAmmo = self.TraitsImplementing<Demolition>().First().Info.UseAmmo;

			// BUG, SF demolishing building
			/* 	Exception has occurred: CLR/System.InvalidOperationException
			An unhandled exception of type 'System.InvalidOperationException' occurred in System.Linq.dll: 'Sequence contains no matching element'
			at System.Linq.ThrowHelper.ThrowNoMatchException()
			at System.Linq.Enumerable.First[TSource](IEnumerable`1 source, Func`2 predicate)
			at OpenRA.Mods.Common.Activities.Demolish.TryStartEnter(Actor self, Actor targetActor) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Activities\Demolish.cs:line 54
			at OpenRA.Mods.Common.Activities.Enter.Tick(Actor self) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Mods.Common\Activities\Enter.cs:line 111
			at OpenRA.Activities.Activity.TickOuter(Actor self) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Activities\Activity.cs:line 120
			at OpenRA.Traits.ActivityUtils.RunActivity(Actor self, Activity act) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Traits\ActivityUtils.cs:line 31
			at OpenRA.Actor.Tick() in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Actor.cs:line 288
			at OpenRA.World.Tick() in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\World.cs:line 449
			at OpenRA.Game.InnerLogicTick(OrderManager orderManager) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Game.cs:line 627
			at OpenRA.Game.LogicTick() in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Game.cs:line 651
			at OpenRA.Game.Loop() in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Game.cs:line 823
			at OpenRA.Game.Run() in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Game.cs:line 876
			at OpenRA.Game.InitializeAndRun(String[] args) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Game\Game.cs:line 308
			at OpenRA.Launcher.Program.Main(String[] args) in C:\Users\fredr\Desktop\WW3MOD\engine\OpenRA.Launcher\Program.cs:line 26 */
			if (!enterDemolishables.Any(i => i.IsValidTarget(enterActor, self)) || !self.TraitsImplementing<AmmoPool>().First(ap => ap.Info.Name == useAmmo).HasAmmo)
			{
				Cancel(self, true);
				return false;
			}

			return true;
		}

		protected override void OnEnterComplete(Actor self, Actor targetActor)
		{
			self.World.AddFrameEndTask(w =>
			{
				// Make sure the target hasn't changed while entering
				// OnEnterComplete is only called if targetActor is alive
				if (targetActor != enterActor)
					return;

				if (!enterDemolishables.Any(i => i.IsValidTarget(enterActor, self)))
					return;

				var useAmmo = self.TraitsImplementing<Demolition>().First().Info.UseAmmo;
				var ammopool = self.TraitsImplementing<AmmoPool>().First(ap => ap.Info.Name == useAmmo);
				ammopool.TakeAmmo(self, 1);

				w.Add(new FlashTarget(enterActor, Color.White, count: flashes, interval: flashInterval, delay: flashesDelay));

				foreach (var ind in notifiers)
					ind.Demolishing(self);

				foreach (var d in enterDemolishables)
					d.Demolish(enterActor, self, delay, damageTypes);

				if (enterBehaviour == EnterBehaviour.Dispose)
					self.Dispose();
				else if (enterBehaviour == EnterBehaviour.Suicide)
					self.Kill(self);
			});
		}
	}
}
