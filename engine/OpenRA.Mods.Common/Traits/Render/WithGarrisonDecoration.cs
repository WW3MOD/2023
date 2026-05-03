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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.Render
{
	[Desc("Renders garrison soldier pips below the building (one column per occupant + empty slots, with class/health/ammo stacked).")]
	public class WithGarrisonDecorationInfo : WithDecorationBaseInfo, Requires<GarrisonManagerInfo>, Requires<CargoInfo>
	{
		[Desc("Image that defines the class pip sequences.")]
		public readonly string Image = "class";

		[Desc("Image that defines the status pip sequences (health/ammo).")]
		public readonly string StatusImage = "pips";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for empty pips (no soldier in slot).")]
		public readonly string EmptySequence = "empty_class";

		[SequenceReference(nameof(Image))]
		[Desc("Sequence used for soldiers without a CustomPipType.")]
		public readonly string FullSequence = "unknown_class";

		[SequenceReference(nameof(StatusImage))]
		public readonly string HealthFullSequence = "pip-green";

		[SequenceReference(nameof(StatusImage))]
		public readonly string HealthLightSequence = "pip-yellow";

		[SequenceReference(nameof(StatusImage))]
		public readonly string HealthMediumSequence = "pip-yellow";

		[SequenceReference(nameof(StatusImage))]
		public readonly string HealthHeavySequence = "pip-red";

		[SequenceReference(nameof(StatusImage))]
		public readonly string HealthCriticalSequence = "pip-red";

		[SequenceReference(nameof(StatusImage))]
		public readonly string AmmoFullSequence = "pip-green";

		[SequenceReference(nameof(StatusImage))]
		public readonly string AmmoLowSequence = "pip-yellow";

		[SequenceReference(nameof(StatusImage))]
		public readonly string AmmoEmptySequence = "pip-red";

		[PaletteReference]
		public readonly string Palette = "chrome";

		[Desc("Number of pips per row.")]
		public readonly int PerRow = 8;

		public override object Create(ActorInitializer init) { return new WithGarrisonDecoration(init.Self, this); }
	}

	public class WithGarrisonDecoration : WithDecorationBase<WithGarrisonDecorationInfo>
	{
		// Per-soldier vertical pip stack: class on bottom (preserves original anchor), then health, then ammo on top.
		const int PipsPerSoldier = 3;

		readonly Animation classPips;
		readonly Animation statusPips;
		readonly Cargo cargo;

		GarrisonManager garrisonManager;

		public WithGarrisonDecoration(Actor self, WithGarrisonDecorationInfo info)
			: base(self, info)
		{
			classPips = new Animation(self.World, info.Image);
			statusPips = new Animation(self.World, info.StatusImage);
			cargo = self.Trait<Cargo>();
		}

		protected override void Created(Actor self)
		{
			base.Created(self);
			garrisonManager = self.Trait<GarrisonManager>();
		}

		int TotalSoldierCount()
		{
			if (garrisonManager == null)
				return 0;

			var count = 0;
			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
				if (garrisonManager.PortStates[i].DeployedSoldier != null && !garrisonManager.PortStates[i].DeployedSoldier.IsDead)
					count++;
			count += garrisonManager.ShelterPassengers.Count(s => s != null && !s.IsDead);
			return count;
		}

		// Collect all soldiers: deployed at ports + shelter (in cargo)
		IEnumerable<Actor> AllSoldiers()
		{
			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var soldier = garrisonManager.PortStates[i].DeployedSoldier;
				if (soldier != null && !soldier.IsDead)
					yield return soldier;
			}

			foreach (var soldier in garrisonManager.ShelterPassengers)
				if (soldier != null && !soldier.IsDead)
					yield return soldier;
		}

		string GetClassSequence(Actor soldier)
		{
			var pi = soldier.Info.TraitInfoOrDefault<PassengerInfo>();
			if (pi?.CustomPipType != null)
				return pi.CustomPipType;
			return Info.FullSequence;
		}

		string GetHealthSequence(Actor soldier)
		{
			var hp = soldier.TraitOrDefault<IHealth>();
			if (hp == null)
				return Info.HealthFullSequence;

			switch (hp.DamageState)
			{
				case DamageState.Light: return Info.HealthLightSequence;
				case DamageState.Medium: return Info.HealthMediumSequence;
				case DamageState.Heavy: return Info.HealthHeavySequence;
				case DamageState.Critical: return Info.HealthCriticalSequence;
				default: return Info.HealthFullSequence;
			}
		}

		string GetAmmoSequence(Actor soldier)
		{
			var pools = soldier.TraitsImplementing<AmmoPool>().ToArray();
			if (pools.Length == 0)
				return null;

			var totalCurrent = 0;
			var totalMax = 0;
			foreach (var p in pools)
			{
				totalCurrent += p.CurrentAmmoCount;
				totalMax += p.Info.Ammo;
			}

			if (totalMax == 0)
				return null;

			// 3-step thresholds matching the 3-pip ammo bar elsewhere.
			var ratio = (double)totalCurrent / totalMax;
			if (ratio > 0.66) return Info.AmmoFullSequence;
			if (ratio > 0.33) return Info.AmmoLowSequence;
			return Info.AmmoEmptySequence;
		}

		// Pips decoration (below actor, screen-space, via WithDecorationBase/IDecoration)
		protected override IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 screenPos)
		{
			if (garrisonManager == null)
				yield break;

			var totalCount = TotalSoldierCount();
			if (totalCount == 0)
				yield break;

			var maxSlots = cargo.Info.MaxWeight;
			var slotCount = maxSlots > 0 ? maxSlots : totalCount;

			var selected = self.World.Selection.Contains(self);
			var scale = selected ? 1f : 0.7f;
			var alpha = selected ? 0.8f : 0.35f;

			var palette = wr.Palette(Info.Palette);

			// Prime animations to read sprite sizes.
			classPips.PlayRepeating(Info.EmptySequence);
			statusPips.PlayRepeating(Info.HealthFullSequence);

			var classImageSize = classPips.Image.Size;
			var statusImageSize = statusPips.Image.Size;

			// Slot dimensions track the class pip (the dominant element). Status pips use the same slot
			// width/height so the column stays uniform and spacing scales with the overall scale factor.
			var slotPipSize = new int2((int)(classImageSize.X * scale), (int)(classImageSize.Y * scale));
			var statusPipSize = new int2((int)(statusImageSize.X * scale), (int)(statusImageSize.Y * scale));

			var pipStrideX = new int2(slotPipSize.X, 0);
			var pipStrideY = new int2(0, slotPipSize.Y);
			var rowStrideY = new int2(0, slotPipSize.Y * PipsPerSoldier);

			var soldiers = AllSoldiers().ToArray();

			var currentRow = 1;
			var currentRowCount = (currentRow * Info.PerRow) > slotCount ? (slotCount % Info.PerRow) : Info.PerRow;

			screenPos -= slotPipSize / 2;
			var startPos = screenPos;
			screenPos -= (currentRowCount - 1) * pipStrideX / 2;

			// Center the (smaller) status pip horizontally inside the slot column.
			var statusOffsetX = (slotPipSize.X - statusPipSize.X) / 2;

			for (var i = 0; i < slotCount; i++)
			{
				var soldier = i < soldiers.Length ? soldiers[i] : null;

				// Class pip — bottom of the slot column (same Y as the previous single-pip layout).
				classPips.PlayRepeating(soldier != null ? GetClassSequence(soldier) : Info.EmptySequence);
				yield return new UISpriteRenderable(
					classPips.Image, self.CenterPosition, screenPos, 0, palette, scale, alpha);

				if (soldier != null)
				{
					// Health pip — one row above class.
					var healthSeq = GetHealthSequence(soldier);
					if (healthSeq != null)
					{
						statusPips.PlayRepeating(healthSeq);
						var healthPos = new int2(screenPos.X + statusOffsetX, screenPos.Y - slotPipSize.Y);
						yield return new UISpriteRenderable(
							statusPips.Image, self.CenterPosition, healthPos, 0, palette, scale, alpha);
					}

					// Ammo pip — top of the slot column.
					var ammoSeq = GetAmmoSequence(soldier);
					if (ammoSeq != null)
					{
						statusPips.PlayRepeating(ammoSeq);
						var ammoPos = new int2(screenPos.X + statusOffsetX, screenPos.Y - 2 * slotPipSize.Y);
						yield return new UISpriteRenderable(
							statusPips.Image, self.CenterPosition, ammoPos, 0, palette, scale, alpha);
					}
				}

				if (i + 1 >= currentRow * Info.PerRow)
				{
					screenPos = startPos - (rowStrideY * currentRow);
					currentRow++;
					currentRowCount = (currentRow * Info.PerRow) > slotCount ? (slotCount % Info.PerRow) : Info.PerRow;
					screenPos -= (currentRowCount - 1) * pipStrideX / 2;
				}
				else
				{
					screenPos += pipStrideX;
				}
			}
		}
	}
}
