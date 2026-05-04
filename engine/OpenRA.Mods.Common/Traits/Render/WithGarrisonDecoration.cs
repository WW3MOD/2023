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
		[Desc("Reference sequence used to measure status pip dimensions.")]
		public readonly string StatusSizeReferenceSequence = "pip-empty";

		[SequenceReference(nameof(StatusImage))]
		public readonly string DamageLightSequence = "pip-damage-infantry-light";

		[SequenceReference(nameof(StatusImage))]
		public readonly string DamageMediumSequence = "pip-damage-infantry-medium";

		[SequenceReference(nameof(StatusImage))]
		public readonly string DamageHeavySequence = "pip-damage-infantry-heavy";

		[SequenceReference(nameof(StatusImage))]
		public readonly string DamageCriticalSequence = "pip-damage-infantry-critical";

		[PaletteReference]
		public readonly string Palette = "chrome";

		[Desc("Maximum pips per row. Actual columns are computed dynamically as ceil(capacity/2) clamped to this value, " +
			"so capacity 4 → 2 cols (2x2), 6 → 3 cols (3x2), 8 → 4 cols (4x2), 12 → 6 cols (6x2).")]
		public readonly int PerRow = 6;

		public override object Create(ActorInitializer init) { return new WithGarrisonDecoration(init.Self, this); }
	}

	public class WithGarrisonDecoration : WithDecorationBase<WithGarrisonDecorationInfo>
	{
		// Per-soldier vertical layout: damage pip on top (when damaged), class pip in the middle
		// (preserves the original anchor Y), ammo row on bottom. Slot height stays 3 rows tall
		// regardless of damage state so the grid stays aligned.
		const int SlotRows = 3;
		const int DamageRow = 0;  // top
		const int ClassRow = 1;   // middle (was the original single-pip Y)
		const int AmmoRow = 2;    // bottom

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

		IEnumerable<Actor> AllSoldiers()
		{
			for (var i = 0; i < garrisonManager.PortStates.Length; i++)
			{
				var soldier = garrisonManager.PortStates[i].DeployedSoldier;
				if (soldier != null && !soldier.IsDead)
					yield return soldier;
			}

			// Sort shelter by ActorID (spawn order) so pip positions stay stable when soldiers
			// cycle through ports — internal Add/Remove churn would otherwise visually swap pips.
			foreach (var soldier in garrisonManager.ShelterPassengers
				.Where(s => s != null && !s.IsDead)
				.OrderBy(s => s.ActorID))
				yield return soldier;
		}

		string GetClassSequence(Actor soldier)
		{
			var pi = soldier.Info.TraitInfoOrDefault<PassengerInfo>();
			if (pi?.CustomPipType != null)
				return pi.CustomPipType;
			return Info.FullSequence;
		}

		string GetDamageSequence(Actor soldier)
		{
			var hp = soldier.TraitOrDefault<IHealth>();
			if (hp == null)
				return null;

			switch (hp.DamageState)
			{
				case DamageState.Light: return Info.DamageLightSequence;
				case DamageState.Medium: return Info.DamageMediumSequence;
				case DamageState.Heavy: return Info.DamageHeavySequence;
				case DamageState.Critical: return Info.DamageCriticalSequence;
				default: return null;
			}
		}

		struct AmmoRecipe
		{
			public int PipCount;
			public string FullSeq;
			public string EmptySeq;
			public int CurrentAmmo;
			public int TotalAmmo;
		}

		// Reads each soldier's own WithAmmoPipsDecoration recipes so the basement view
		// shows the same pips the soldier displays normally (3 yellow primary, 1 red secondary, etc.).
		// Disabled traits (RequiresCondition not met — typically out of ammo) are skipped.
		static IEnumerable<AmmoRecipe> GetAmmoRecipes(Actor soldier)
		{
			var allPools = soldier.TraitsImplementing<AmmoPool>().ToArray();
			foreach (var deco in soldier.TraitsImplementing<WithAmmoPipsDecoration>())
			{
				if (deco.IsTraitDisabled)
					continue;

				var info = deco.Info;
				var pools = info.AmmoPools.Length > 0
					? allPools.Where(p => info.AmmoPools.Contains(p.Info.Name)).ToArray()
					: allPools;

				if (pools.Length == 0)
					continue;

				var current = 0;
				var total = 0;
				foreach (var p in pools)
				{
					current += p.CurrentAmmoCount;
					total += p.Info.Ammo;
				}

				if (total == 0)
					continue;

				var pipCount = info.PipCount > 0 ? info.PipCount : total;

				yield return new AmmoRecipe
				{
					PipCount = pipCount,
					FullSeq = info.FullSequence,
					EmptySeq = info.EmptySequence,
					CurrentAmmo = current,
					TotalAmmo = total,
				};
			}
		}

		protected override IEnumerable<IRenderable> RenderDecoration(Actor self, WorldRenderer wr, int2 screenPos)
		{
			if (garrisonManager == null)
				yield break;

			var totalCount = TotalSoldierCount();
			if (totalCount == 0)
				yield break;

			// Render only filled slots so the pips visually center on the building
			// instead of left-aligning with empty placeholders trailing to the right.
			// Column count derives from capacity (so 2x2 / 3x2 / 4x2 / 6x2 grids stay
			// stable across builds), capped at Info.PerRow.
			var maxSlots = cargo.Info.MaxWeight;
			var capacity = maxSlots > 0 ? maxSlots : totalCount;
			var perRow = Math.Max(1, Math.Min(Info.PerRow, (capacity + 1) / 2));
			var slotCount = totalCount;

			var selected = self.World.Selection.Contains(self);
			var scale = selected ? 1f : 0.7f;
			var alpha = selected ? 0.8f : 0.35f;

			var palette = wr.Palette(Info.Palette);

			classPips.PlayRepeating(Info.EmptySequence);
			statusPips.PlayRepeating(Info.StatusSizeReferenceSequence);

			var classImageSize = classPips.Image.Size;
			var statusImageSize = statusPips.Image.Size;

			var classPipSize = new int2((int)(classImageSize.X * scale), (int)(classImageSize.Y * scale));
			var statusPipSize = new int2((int)(statusImageSize.X * scale), (int)(statusImageSize.Y * scale));

			// Build the per-soldier ammo recipes once and use them to size the slot.
			// Slot width must accommodate the widest ammo row across all visible soldiers
			// so adjacent columns don't collide.
			var soldiers = AllSoldiers().ToArray();
			var soldierRecipes = new AmmoRecipe[soldiers.Length][];
			var maxAmmoPipsInRow = 0;
			for (var s = 0; s < soldiers.Length; s++)
			{
				var recipes = GetAmmoRecipes(soldiers[s]).ToArray();
				soldierRecipes[s] = recipes;
				var rowPips = 0;
				foreach (var r in recipes)
					rowPips += r.PipCount;
				if (rowPips > maxAmmoPipsInRow)
					maxAmmoPipsInRow = rowPips;
			}

			var ammoRowMaxWidth = maxAmmoPipsInRow * statusPipSize.X;
			var slotWidth = Math.Max(classPipSize.X, ammoRowMaxWidth);
			var rowHeight = classPipSize.Y;
			var slotHeight = SlotRows * rowHeight;

			var slotStrideX = new int2(slotWidth, 0);
			var slotStrideY = new int2(0, slotHeight);

			var currentRow = 1;
			var currentRowCount = (currentRow * perRow) > slotCount ? (slotCount % perRow) : perRow;

			// Anchor the slot center on the original screenPos. The class pip sits at the
			// middle row (its Y matches the previous single-pip layout); damage row is above,
			// ammo row below.
			screenPos -= new int2(slotWidth / 2, classPipSize.Y / 2);
			var startPos = screenPos;
			screenPos -= (currentRowCount - 1) * slotStrideX / 2;

			// SpriteRenderer renders each sprite at (location + scale * sprite.Offset) with size
			// (scale * sprite.Size). Different sprite sheets (class icon vs status pip) have
			// different intrinsic Size and Offset, so a "subtract half the reference size" approach
			// (the previous one) leaves damage/ammo pips visibly offset from the class pip beneath.
			// Each render below derives its top-left from the actual sprite's Size+Offset so all
			// pips share the same visible center.
			int2 CenteredScreenPos(Sprite s, int targetX, int targetY)
			{
				return new int2(
					targetX - (int)(s.Size.X * scale * 0.5f) - (int)(s.Offset.X * scale),
					targetY - (int)(s.Size.Y * scale * 0.5f) - (int)(s.Offset.Y * scale));
			}

			for (var i = 0; i < slotCount; i++)
			{
				var soldier = i < soldiers.Length ? soldiers[i] : null;
				var slotCenterX = screenPos.X + slotWidth / 2;

				// Y center of each row (each row is one rowHeight tall).
				var classCenterY = screenPos.Y + (ClassRow - ClassRow) * rowHeight + rowHeight / 2;
				var damageCenterY = screenPos.Y + (DamageRow - ClassRow) * rowHeight + rowHeight / 2;
				var ammoCenterY = screenPos.Y + (AmmoRow - ClassRow) * rowHeight + rowHeight / 2;

				// Class pip — middle row.
				classPips.PlayRepeating(soldier != null ? GetClassSequence(soldier) : Info.EmptySequence);
				var classSprite = classPips.Image;
				yield return new UISpriteRenderable(
					classSprite, self.CenterPosition,
					CenteredScreenPos(classSprite, slotCenterX, classCenterY),
					0, palette, scale, alpha);

				if (soldier != null)
				{
					// Damage pip — top row, only when damaged.
					var damageSeq = GetDamageSequence(soldier);
					if (damageSeq != null)
					{
						statusPips.PlayRepeating(damageSeq);
						var damageSprite = statusPips.Image;
						yield return new UISpriteRenderable(
							damageSprite, self.CenterPosition,
							CenteredScreenPos(damageSprite, slotCenterX, damageCenterY),
							0, palette, scale, alpha);
					}

					// Ammo row — bottom row. Concatenate every recipe horizontally and center the whole row.
					var recipes = soldierRecipes[i];
					var totalAmmoPips = 0;
					foreach (var r in recipes)
						totalAmmoPips += r.PipCount;

					if (totalAmmoPips > 0)
					{
						// Use the reference pip's stride for layout consistency, but each pip is
						// centered via its own Size+Offset for precise alignment.
						var rowWidth = totalAmmoPips * statusPipSize.X;
						var ammoStartCenterX = slotCenterX - rowWidth / 2 + statusPipSize.X / 2;

						var pipIndex = 0;
						foreach (var recipe in recipes)
						{
							for (var p = 0; p < recipe.PipCount; p++)
							{
								// Same fill rule as WithAmmoPipsDecoration: pip p is full when
								// currentAmmo * pipCount > p * totalAmmo.
								var seq = recipe.CurrentAmmo * recipe.PipCount > p * recipe.TotalAmmo
									? recipe.FullSeq : recipe.EmptySeq;
								statusPips.PlayRepeating(seq);
								var ammoSprite = statusPips.Image;
								var ammoCenterX = ammoStartCenterX + pipIndex * statusPipSize.X;
								yield return new UISpriteRenderable(
									ammoSprite, self.CenterPosition,
									CenteredScreenPos(ammoSprite, ammoCenterX, ammoCenterY),
									0, palette, scale, alpha);
								pipIndex++;
							}
						}
					}
				}

				if (i + 1 >= currentRow * perRow)
				{
					screenPos = startPos - (slotStrideY * currentRow);
					currentRow++;
					currentRowCount = (currentRow * perRow) > slotCount ? (slotCount % perRow) : perRow;
					screenPos -= (currentRowCount - 1) * slotStrideX / 2;
				}
				else
				{
					screenPos += slotStrideX;
				}
			}
		}
	}
}
