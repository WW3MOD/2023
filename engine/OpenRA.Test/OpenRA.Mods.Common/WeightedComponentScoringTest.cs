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

using NUnit.Framework;
using OpenRA.Mods.Common.Tournament;
using OpenRA.Mods.Common.Tournament.Scorers;

namespace OpenRA.Test
{
	/// <summary>
	/// Covers the default scorer's weighting math (WeightedComponentScoring), factored out
	/// of WeightedComponentMatchScorer so it can be validated without a World. The
	/// load-bearing property (LADDER S1 follow-up 1a, verdict_version 4): the economy term
	/// "capture_income" derives from GROSS building income, not net PlayerResources.Earned.
	/// </summary>
	[TestFixture]
	public class WeightedComponentScoringTest
	{
		static TournamentConfig.ScoreConfig Weights(float army, float capture, float kills)
		{
			return new TournamentConfig.ScoreConfig
			{
				ArmyValueWeight = army,
				CaptureIncomeWeight = capture,
				KillsValueWeight = kills,
			};
		}

		[Test]
		public void EachComponentIsWeightedIndependently()
		{
			var s = WeightedComponentScoring.Compute(100, 200, 300, Weights(1f, 2f, 3f));
			Assert.That(s.Components["army_value"], Is.EqualTo(100L));
			Assert.That(s.Components["capture_income"], Is.EqualTo(400L));
			Assert.That(s.Components["kills_value"], Is.EqualTo(900L));
		}

		[Test]
		public void TotalIsTheSumOfWeightedComponents()
		{
			var s = WeightedComponentScoring.Compute(100, 200, 300, Weights(1f, 2f, 3f));
			Assert.That(s.Total, Is.EqualTo(100L + 400L + 900L));
		}

		[Test]
		public void CaptureIncomeTracksGrossInput()
		{
			// The economy term reflects the gross capture-income figure it is handed. This is
			// the whole point of 1a: a bot holding a $5950-gross derrick contributes to the
			// economy axis, where net Earned would have fed 0.
			var s = WeightedComponentScoring.Compute(0, 5950, 0, Weights(1f, 2f, 1f));
			Assert.That(s.Components["capture_income"], Is.EqualTo(11900L));
			Assert.That(s.Total, Is.EqualTo(11900L));
		}

		[Test]
		public void ZeroGrossGivesZeroEconomyTerm()
		{
			// A control that never captures (gross 0) scores 0 on the economy axis regardless
			// of the weight — mirrors Normal in S1.
			var s = WeightedComponentScoring.Compute(0, 0, 0, Weights(1f, 2f, 1f));
			Assert.That(s.Components["capture_income"], Is.EqualTo(0L));
			Assert.That(s.Total, Is.EqualTo(0L));
		}

		[Test]
		public void FractionalWeightTruncatesTowardZero()
		{
			// Matches the historical (long) cast behaviour of the scorer.
			var s = WeightedComponentScoring.Compute(0, 3, 0, Weights(1f, 0.5f, 1f));
			Assert.That(s.Components["capture_income"], Is.EqualTo(1L)); // (long)(3 * 0.5) = 1
		}
	}
}
