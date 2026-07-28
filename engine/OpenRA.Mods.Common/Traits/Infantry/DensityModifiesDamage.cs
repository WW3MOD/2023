#region Copyright & License Information
/*
 * WW3MOD (item 26 phase 2): tree-density-aware cover damage reduction. A sibling of the
 * dormant TerrainModifiesDamage seam, which keys on the PAINTED terrain type of the actor's
 * cell — useless for forest cover, because tree actors sit on Clear tiles (see recon
 * 260728-trees-concealment.md Q5). This variant instead reads the same Map.DensityLayer that
 * feeds shadow concealment, so a unit standing AMONG dense trees takes modestly reduced damage.
 *
 * Cover signal = summed tree density in a (2*SampleRadius+1)^2 window centred on the actor's
 * cell — the actor's own cell plus its neighbours. This mirrors CohesionMoveModifier.CoverScore
 * (a unit is "in the trees" when surrounded by density, whether it stands on a trunk cell or a
 * passable gap between trunks), so it composes with the shipped stance cover-positioning.
 *
 * Pure integer, deterministic, zero RNG (sim-path safe). Global by design — affects humans and
 * bots alike (item 26); combat outcomes shift, so the AI benchmark must be re-baselined (item 25).
 */
#endregion

using System.Collections.Generic;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Reduces damage taken based on nearby tree density (Map.DensityLayer) — forest cover.")]
	public class DensityModifiesDamageInfo : TraitInfo
	{
		[FieldLoader.Require]
		[Desc("Map of minimum windowed tree density -> damage percentage. 100 = full damage, 85 = 15% reduction.",
			"The highest threshold whose key is <= the actor's windowed density wins. Example:",
			"{ 15: 94, 25: 88, 40: 80 } — light cover 94%, moderate 88%, deep forest 80%.")]
		public readonly Dictionary<int, int> DensityModifier = null;

		[Desc("Half-width of the square density sampling window around the actor's cell. 1 = 3x3.")]
		public readonly int SampleRadius = 1;

		[Desc("Modify healing damage? For example: a friendly medic.")]
		public readonly bool ModifyHealing = false;

		public override object Create(ActorInitializer init) { return new DensityModifiesDamage(init.Self, this); }
	}

	public class DensityModifiesDamage : IDamageModifier
	{
		const int FullDamage = 100;

		public readonly DensityModifiesDamageInfo Info;

		readonly Actor self;

		public DensityModifiesDamage(Actor self, DensityModifiesDamageInfo info)
		{
			Info = info;
			this.self = self;
		}

		int IDamageModifier.GetDamageModifier(Actor attacker, Damage damage)
		{
			// Never dampen healing (negative damage) unless explicitly opted in — cover shouldn't
			// weaken a medic. Simpler and strictly safer than the sibling trait's allied-attacker
			// gate, which crashed on a null attacker and left null-source healing modifiable.
			if (!Info.ModifyHealing && damage != null && damage.Value < 0)
				return FullDamage;

			var map = self.World.Map;
			if (map.DensityLayer == null)
				return FullDamage;

			var cell = map.CellContaining(self.CenterPosition);
			var windowedDensity = 0;
			for (var dy = -Info.SampleRadius; dy <= Info.SampleRadius; dy++)
			{
				for (var dx = -Info.SampleRadius; dx <= Info.SampleRadius; dx++)
				{
					var c = new CPos(cell.X + dx, cell.Y + dy);
					if (map.DensityLayer.IsValidCoordinate(c.X, c.Y))
						windowedDensity += map.DensityLayer[c];
				}
			}

			return SelectModifier(Info.DensityModifier, windowedDensity);
		}

		/// <summary>
		/// Pick the damage percentage for a given windowed tree density: the value of the HIGHEST
		/// threshold whose key is &lt;= <paramref name="windowedDensity"/>. Below every threshold →
		/// full damage (100). Order-independent and safe for unsorted dictionaries. Pure integer.
		/// </summary>
		public static int SelectModifier(IReadOnlyDictionary<int, int> thresholds, int windowedDensity)
		{
			var result = FullDamage;
			var bestThreshold = int.MinValue;
			foreach (var kv in thresholds)
			{
				if (windowedDensity >= kv.Key && kv.Key > bestThreshold)
				{
					bestThreshold = kv.Key;
					result = kv.Value;
				}
			}

			return result;
		}
	}
}
