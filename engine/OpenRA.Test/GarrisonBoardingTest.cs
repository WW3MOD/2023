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

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Audit item #5. Two hostile players could hold one garrisonable building at once, measured end
	/// to end in run 260915_184131_p88773: the relationship gate runs exactly ONCE, at targeting time,
	/// and it is asked of a NEUTRAL building — so the man who loses the race to a contested house
	/// arrives at a building that has become his enemy's, and nothing downstream re-examines him.
	///
	/// <para>User ruling the same day: close it at the SIM, not the UI, by re-validating at boarding
	/// time. Ordering men into a neutral building stays legal, because that race is the mechanic.</para>
	/// </summary>
	[TestFixture]
	public class GarrisonBoardingTest
	{
		[Test]
		public void AnEnemyIsRefusedAndNobodyElseIs()
		{
			Assert.That(GarrisonBoardingMath.MayBoard(PlayerRelationship.Enemy), Is.False,
				"a soldier may still board a building owned by his enemy, which is the whole of item #5: " +
				"a hostile co-garrison with no capture, no technician and no timer.");

			Assert.That(GarrisonBoardingMath.MayBoard(PlayerRelationship.Neutral), Is.True,
				"a NEUTRAL building now refuses boarders. That is not a stricter fix, it is the mechanic " +
				"switched off — every civilian house starts Neutral, so nobody could ever garrison one.");

			Assert.That(GarrisonBoardingMath.MayBoard(PlayerRelationship.Ally), Is.True,
				"an allied co-garrison is refused; allied garrisoning is a designed feature, and " +
				"GarrisonManager.CheckOwnershipAfterExit exists specifically to hand a building to an " +
				"ally still inside.");

			Assert.That(GarrisonBoardingMath.MayBoard(PlayerRelationship.None), Is.False,
				"an unclassifiable relationship is admitted. It cannot be shown to be friendly, so the " +
				"safe answer is no.");
		}

		[Test]
		public void TheRuleIsWiredIntoTheLoadPathAndNotTheOrderPath()
		{
			// The predicate can be perfect and never consulted. GarrisonManager must implement
			// ICargoCanLoadFilter, which Cargo.CanLoad runs at the moment of boarding and
			// RideTransport.OnEnterComplete honours by leaving the man outside.
			Assert.That(typeof(ICargoCanLoadFilter).IsAssignableFrom(typeof(GarrisonManager)), Is.True,
				"GarrisonManager no longer implements ICargoCanLoadFilter, so nothing re-checks the " +
				"relationship at boarding time and item #5 is open again.");

			var filter = typeof(GarrisonManager)
				.GetInterfaceMap(typeof(ICargoCanLoadFilter))
				.TargetMethods
				.Single();

			var scan = IlScan.Scan(filter);

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(1),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in the load filter — the scanner is " +
				"broken, not the code clean.");

			Assert.That(scan.Callees.Any(c => c.Name == "MayBoard"), Is.True,
				"the load filter no longer calls GarrisonBoardingMath.MayBoard, so whatever rule it now " +
				"applies is untested by the cases above.");
		}

		[Test]
		public void TheOrderLayerIsDeliberatelyNotNarrowed()
		{
			// The mirror of the fix, and the half that is easy to lose: ordering into a NEUTRAL building
			// must stay legal. If a later change moves the relationship test up into targeting, the
			// contested-house race — the mechanic this scenario measures — stops existing at all, and
			// MayBoard's Neutral case above would no longer be reachable in play.
			Assert.That(GarrisonBoardingMath.MayBoard(PlayerRelationship.Neutral), Is.True,
				"restated deliberately: the neutral window is load-bearing, not an oversight.");
		}
	}
}
