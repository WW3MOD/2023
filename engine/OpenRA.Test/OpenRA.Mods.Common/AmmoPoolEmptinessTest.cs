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

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Contract for AmmoPool.AllPoolsEmpty — the single definition of "this actor cannot shoot".
	///
	/// This exists because the wrong predicate passed review once already. The tempting alternative,
	/// Rearmable.RearmableAmmoPools, is filtered to Rearmable.AmmoPools and answers "which pools can a
	/// host refill for me" — a different question that happens to give the same answer on 13 of
	/// WW3MOD's 14 armed infantry classes. The fourteenth is the combat engineer, who declares only his
	/// C4 charges as rearmable while also carrying an SMG pool, so the filtered set calls him empty
	/// with a full magazine. MixedPoolsAreNotEmpty below is that case.
	/// </summary>
	[TestFixture]
	public class AmmoPoolEmptinessTest
	{
		static AmmoPool Pool(int capacity, int initial)
		{
			// The Info fields are readonly, which is what FieldLoader exists to populate.
			var info = new AmmoPoolInfo();
			FieldLoader.LoadField(info, "Ammo", capacity.ToString());
			FieldLoader.LoadField(info, "InitialAmmo", initial.ToString());
			return new AmmoPool(info);
		}

		[Test]
		public void NoPoolsIsNotEmpty()
		{
			// An actor with no ammunition at all (medic, technician) has infinite ammo as far as this
			// question goes — it must never read as "out of ammo" or every unarmed class walks off to
			// find a supply truck it has no use for.
			Assert.That(AmmoPool.AllPoolsEmpty(new List<AmmoPool>()), Is.False);
		}

		[Test]
		public void SingleEmptyPoolIsEmpty()
		{
			Assert.That(AmmoPool.AllPoolsEmpty(new[] { Pool(100, 0) }), Is.True);
		}

		[Test]
		public void SingleLoadedPoolIsNotEmpty()
		{
			Assert.That(AmmoPool.AllPoolsEmpty(new[] { Pool(100, 100) }), Is.False);
			Assert.That(AmmoPool.AllPoolsEmpty(new[] { Pool(100, 1) }), Is.False);
		}

		[Test]
		public void AllPoolsEmptyIsEmpty()
		{
			Assert.That(AmmoPool.AllPoolsEmpty(new[] { Pool(100, 0), Pool(3, 0) }), Is.True);
		}

		[Test]
		public void MixedPoolsAreNotEmpty()
		{
			// The ^E6 regression. First ordering is the engineer: SMG full, C4 spent. Second is the
			// rifleman: rifle spent, RPG round left. Neither man is out of ammo, and neither should
			// break off his order — so BOTH orderings must answer false, not just the one where the
			// loaded pool happens to come first and short-circuits the loop.
			Assert.That(AmmoPool.AllPoolsEmpty(new[] { Pool(100, 100), Pool(3, 0) }), Is.False);
			Assert.That(AmmoPool.AllPoolsEmpty(new[] { Pool(100, 0), Pool(1, 1) }), Is.False);
		}
	}
}
