# Crew & Passenger Evacuation Rework — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify crew and passenger ejection through one shared resolver, add HP-driven `onfire` stacks to vehicles + helicopters, flip helicopter emergency landing to critical-only with mid-glide death rules, and remove the re-entry feature so burning vehicles are unrecoverable.

**Architecture:** Two new files (`EvacResolver` static helper + `OnFireFromHealth` trait) carry the new mechanics. `VehicleCrew`, `Cargo`, and `HeliEmergencyLanding` are refactored to call into them. YAML adds `onfire` stacks + 5-tier visual overlays + per-vehicle `CrewFireTransferPct` knobs to `^Vehicle` / `^Aircraft` defaults. Re-entry path (`CrewMember.cs`, `EnterAsCrew.cs`, `AllowForeignCrew`, capture-by-pilot) is deleted.

**Tech Stack:** C# 10 / .NET 6 (OpenRA engine), NUnit 3 (`engine/OpenRA.Test/`), MiniYaml (`mods/ww3mod/rules/`). Build: `./make.ps1 all` from project root. Tests: `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release`.

**Spec:** `CLAUDE/plans/260506_crew_evac_design.md` — read this first if unfamiliar with the design.

---

## File map (decomposition reference)

**New (engine):**
- `engine/OpenRA.Mods.Common/Traits/EvacResolver.cs` — pure-function math helper (~50 lines).
- `engine/OpenRA.Mods.Common/Traits/OnFireFromHealth.cs` — grants self `onfire` external condition stacks based on HP threshold bands.
- `engine/OpenRA.Test/OpenRA.Mods.Common/EvacResolverTest.cs` — math unit tests.

**Modified (engine):**
- `engine/OpenRA.Mods.Common/Traits/VehicleCrew.cs` — refactor to use resolver, harden stop detection, drop obsolete fields, remove "repaired out of critical → cancel" branch.
- `engine/OpenRA.Mods.Common/Traits/Cargo.cs` — add critical-state staged eject path + suppress flag.
- `engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs` — remove heavy path, add mid-glide death rules, drop neutral-transfer + capture plumbing.

**Deleted (engine):**
- `engine/OpenRA.Mods.Common/Traits/CrewMember.cs` (re-entry IIssueOrder).
- `engine/OpenRA.Mods.Common/Activities/Air/EnterAsCrew.cs` (re-entry activity).

**Modified (YAML):**
- `mods/ww3mod/rules/ingame/defaults.yaml` — add `OnFireFromHealth`, `ExternalCondition@onfire`, `WithIdleOverlay@Burn_1..5`, gate `Repairable*` on `!critical-damage` in `^Vehicle` and `^Aircraft` templates.
- `mods/ww3mod/rules/ingame/vehicles-{america,russia}.yaml` — drop obsolete `VehicleCrew` fields, add `CrewFireTransferPct: 100` + `StoppedTicksRequired: 8`.
- `mods/ww3mod/rules/ingame/aircraft.yaml` — `^Helicopter` template: `CrewFireTransferPct: 0`, new `RotorDestroyDamageThresholdPct` and `RotorDestroyedCondition` on `HeliEmergencyLanding`.
- `mods/ww3mod/rules/ingame/aircraft-{america,russia}.yaml` — per-heli `SpinsOnCrash` confirmations.
- `mods/ww3mod/rules/ingame/crew.yaml` — SMG, ~1/3 ammo, role-based costs, `Selectable: Priority/Class`.

---

## Tuning constants (locked from spec)

| Constant | Value | Where |
|---|---|---|
| Onfire stack threshold | 50% HP | `OnFireFromHealth.StartHealthPct` |
| Onfire stack band | 5% HP per stack | `OnFireFromHealth.BandSize` |
| Stop hysteresis | 8 ticks | `VehicleCrew.StoppedTicksRequired` / `Cargo.StoppedTicksRequired` |
| Post-stop delay (crew) | 38 ticks ± 13 | `VehicleCrew.PostStopDelay` / `EjectionDelayVariance` |
| Crew between-eject | 38 ticks ± 13 | `VehicleCrew.EjectionDelay` / `EjectionDelayVariance` |
| Passenger between-eject | 12 ticks ± 4 | `Cargo.EjectionDelay` / `EjectionDelayVariance` |
| Stop timeout | 150 ticks | `VehicleCrew.StopTimeout` / `Cargo.StopTimeout` |
| `CrewFireTransferPct` (tanks) | 100 | per-vehicle YAML |
| `CrewFireTransferPct` (helis) | 0 | per-vehicle YAML |
| `RotorDestroyDamageThresholdPct` | 50 | `HeliEmergencyLanding` YAML |
| `CrewLethalityScale` | 100 (const) | `EvacResolver` |

---

### Task 1: Create EvacResolver scaffolding + RollDamage tests (TDD)

**Files:**
- Create: `engine/OpenRA.Mods.Common/Traits/EvacResolver.cs`
- Create: `engine/OpenRA.Test/OpenRA.Mods.Common/EvacResolverTest.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// engine/OpenRA.Test/OpenRA.Mods.Common/EvacResolverTest.cs
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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Tests the pure math used by VehicleCrew and Cargo to resolve per-occupant
	/// outcomes when a vehicle hits critical or dies. See CLAUDE/plans/260506_crew_evac_design.md.
	/// </summary>
	[TestFixture]
	public class EvacResolverTest
	{
		// --- RollDamage ---

		[Test]
		public void RollDamage_NeutralJitter_ReturnsExpectedFractionOfOccupantHP()
		{
			// finishingFraction = 500/1000 = 0.5, jitter=1.0, occMaxHP=100 → expected 50
			Assert.That(EvacResolver.RollDamage(500, 1000, 100, 100), Is.EqualTo(50));
		}

		[Test]
		public void RollDamage_LuckyJitter_ReturnsZero()
		{
			Assert.That(EvacResolver.RollDamage(500, 1000, 100, 0), Is.EqualTo(0));
		}

		[Test]
		public void RollDamage_UnluckyJitter_DoublesExpected()
		{
			// finishingFraction=0.5, jitter=2.0, occMaxHP=100 → expected 100
			Assert.That(EvacResolver.RollDamage(500, 1000, 100, 200), Is.EqualTo(100));
		}

		[Test]
		public void RollDamage_FinishingFractionAboveTwo_IsCapped()
		{
			// finishingDamage way bigger than 2*maxHP — clamp finishingFraction at 2.
			// jitter=1.0 → expected 2*100 = 200
			Assert.That(EvacResolver.RollDamage(10000, 1000, 100, 100), Is.EqualTo(200));
		}

		[Test]
		public void RollDamage_OutputClampedAtTwoOccupantMaxHP()
		{
			// finFrac=2 (capped from 5x), jitter=2.0 → theoretical 4*occMaxHP, clamp to 2*occMaxHP=200
			Assert.That(EvacResolver.RollDamage(5000, 1000, 100, 200), Is.EqualTo(200));
		}

		[Test]
		public void RollDamage_VehicleMaxHPZero_ReturnsZero()
		{
			Assert.That(EvacResolver.RollDamage(500, 0, 100, 100), Is.EqualTo(0));
		}

		[Test]
		public void RollDamage_LethalThreshold_HpLossEqualsOccupantMaxHP()
		{
			// finFrac=1, jitter=1, occMaxHP=100 → hpLoss=100 → caller treats as dead inside
			Assert.That(EvacResolver.RollDamage(1000, 1000, 100, 100), Is.EqualTo(100));
		}
	}
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release --filter EvacResolverTest
```

Expected: compilation failure (`EvacResolver` not defined).

- [ ] **Step 3: Create EvacResolver with RollDamage**

```csharp
// engine/OpenRA.Mods.Common/Traits/EvacResolver.cs
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

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>
	/// Pure-function math for crew and passenger evacuation outcomes.
	/// See CLAUDE/plans/260506_crew_evac_design.md §4.
	/// </summary>
	public static class EvacResolver
	{
		/// <summary>Global lethality scalar in percent; 100 = baseline. Promoted to YAML if tuning needs it.</summary>
		public const int CrewLethalityScale = 100;

		/// <summary>
		/// Compute HP damage applied to one occupant ejecting from a vehicle.
		/// Caller treats <c>hpLoss &gt;= occupantMaxHP</c> as "dead inside" — no actor spawned.
		/// </summary>
		/// <param name="finishingDamage">Damage value of the shot that pushed the vehicle to critical (or killed it).</param>
		/// <param name="vehicleMaxHP">Vehicle's MaxHP.</param>
		/// <param name="occupantMaxHP">Occupant's MaxHP.</param>
		/// <param name="jitterPercent">Random jitter sample, 0..200. 100 = neutral, &lt;100 lucky, &gt;100 unlucky.</param>
		/// <returns>HP to subtract from occupant's spawn HP. Clamped to [0, 2 * occupantMaxHP].</returns>
		public static int RollDamage(int finishingDamage, int vehicleMaxHP, int occupantMaxHP, int jitterPercent)
		{
			if (vehicleMaxHP <= 0 || occupantMaxHP <= 0)
				return 0;

			// finishingFraction in [0, 2]: cap finishingDamage at 2 * vehicleMaxHP.
			var cappedFinishing = Math.Min(finishingDamage, vehicleMaxHP * 2);

			// hpLoss = finishingFraction * occupantMaxHP * (jitterPercent / 100) * (CrewLethalityScale / 100)
			// Use long to avoid overflow on big finishingDamage.
			var hpLoss = (long)cappedFinishing * occupantMaxHP * jitterPercent * CrewLethalityScale
				/ ((long)vehicleMaxHP * 100 * 100);

			return (int)Math.Clamp(hpLoss, 0L, (long)occupantMaxHP * 2);
		}
	}
}
```

- [ ] **Step 4: Build engine + run tests**

```powershell
./make.ps1 all
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release --filter EvacResolverTest
```

Expected: build succeeds, all 7 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/EvacResolver.cs engine/OpenRA.Test/OpenRA.Mods.Common/EvacResolverTest.cs
git commit -m "EvacResolver: add RollDamage pure-function with unit tests"
```

---

### Task 2: Add InheritOnFireStacks to EvacResolver (TDD)

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/EvacResolver.cs`
- Modify: `engine/OpenRA.Test/OpenRA.Mods.Common/EvacResolverTest.cs`

- [ ] **Step 1: Add failing tests**

Append after the existing `RollDamage` tests in `EvacResolverTest.cs`:

```csharp
		// --- InheritOnFireStacks ---

		[Test]
		public void InheritOnFireStacks_DeathPath_AlwaysTen()
		{
			Assert.That(EvacResolver.InheritOnFireStacks(0, 0, isDeathPath: true), Is.EqualTo(10));
			Assert.That(EvacResolver.InheritOnFireStacks(7, 100, isDeathPath: true), Is.EqualTo(10));
			Assert.That(EvacResolver.InheritOnFireStacks(15, 100, isDeathPath: true), Is.EqualTo(10));
		}

		[Test]
		public void InheritOnFireStacks_StagedTankFullTransfer_ReturnsVehicleStacks()
		{
			Assert.That(EvacResolver.InheritOnFireStacks(7, 100, isDeathPath: false), Is.EqualTo(7));
		}

		[Test]
		public void InheritOnFireStacks_StagedHeliZeroTransfer_ReturnsZero()
		{
			Assert.That(EvacResolver.InheritOnFireStacks(7, 0, isDeathPath: false), Is.EqualTo(0));
		}

		[Test]
		public void InheritOnFireStacks_StagedClampedAtTen()
		{
			Assert.That(EvacResolver.InheritOnFireStacks(12, 100, isDeathPath: false), Is.EqualTo(10));
		}

		[Test]
		public void InheritOnFireStacks_StagedHalfTransfer_HalfStacks()
		{
			Assert.That(EvacResolver.InheritOnFireStacks(8, 50, isDeathPath: false), Is.EqualTo(4));
		}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release --filter EvacResolverTest
```

Expected: compilation failure (`InheritOnFireStacks` not defined).

- [ ] **Step 3: Add the function**

Append to `EvacResolver` class:

```csharp
		/// <summary>
		/// Compute onfire stacks an ejecting occupant inherits from their vehicle.
		/// On the death path, occupants are caught in the explosion regardless of cockpit protection
		/// and always come out at max stacks.
		/// </summary>
		/// <param name="vehicleStacks">Current onfire stacks on the vehicle (0..10).</param>
		/// <param name="transferPct">Cockpit-protection knob: 100 = full inherit (tanks), 0 = no inherit (helis).</param>
		/// <param name="isDeathPath">If true, ignore <paramref name="transferPct"/> and return 10 (engulfed in explosion).</param>
		/// <returns>Stacks to grant to the ejecting occupant, clamped 0..10.</returns>
		public static int InheritOnFireStacks(int vehicleStacks, int transferPct, bool isDeathPath)
		{
			if (isDeathPath)
				return 10;

			return Math.Clamp(vehicleStacks * transferPct / 100, 0, 10);
		}
```

- [ ] **Step 4: Build and run tests**

```powershell
./make.ps1 all
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release --filter EvacResolverTest
```

Expected: 12 tests PASS (7 RollDamage + 5 InheritOnFireStacks).

- [ ] **Step 5: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/EvacResolver.cs engine/OpenRA.Test/OpenRA.Mods.Common/EvacResolverTest.cs
git commit -m "EvacResolver: add InheritOnFireStacks with cockpit-protection + death-path override"
```

---

### Task 3: Add HP-stack mapping helper test + function

The vehicle's onfire stack count is a function of HP%. We test that mapping in isolation as a pure function; the full trait wiring (granting/revoking conditions) follows in Task 4.

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/EvacResolver.cs`
- Modify: `engine/OpenRA.Test/OpenRA.Mods.Common/EvacResolverTest.cs`

- [ ] **Step 1: Add failing tests**

Append to `EvacResolverTest.cs`:

```csharp
		// --- HpToOnFireStacks ---

		[Test]
		public void HpToOnFireStacks_AtFullHealth_ReturnsZero()
		{
			Assert.That(EvacResolver.HpToOnFireStacks(100, startHealthPct: 50, bandSize: 5, maxStacks: 10), Is.EqualTo(0));
		}

		[Test]
		public void HpToOnFireStacks_AtThreshold_ReturnsZero()
		{
			Assert.That(EvacResolver.HpToOnFireStacks(50, startHealthPct: 50, bandSize: 5, maxStacks: 10), Is.EqualTo(0));
		}

		[Test]
		public void HpToOnFireStacks_JustBelowThreshold_ReturnsOne()
		{
			Assert.That(EvacResolver.HpToOnFireStacks(49, startHealthPct: 50, bandSize: 5, maxStacks: 10), Is.EqualTo(1));
		}

		[Test]
		public void HpToOnFireStacks_AtFourPct_ReturnsTen()
		{
			Assert.That(EvacResolver.HpToOnFireStacks(4, startHealthPct: 50, bandSize: 5, maxStacks: 10), Is.EqualTo(10));
		}

		[Test]
		public void HpToOnFireStacks_AtZeroPct_ClampedAtTen()
		{
			Assert.That(EvacResolver.HpToOnFireStacks(0, startHealthPct: 50, bandSize: 5, maxStacks: 10), Is.EqualTo(10));
		}

		[Test]
		public void HpToOnFireStacks_44Pct_ReturnsTwo()
		{
			// ceil((50 - 44) / 5) = ceil(1.2) = 2
			Assert.That(EvacResolver.HpToOnFireStacks(44, startHealthPct: 50, bandSize: 5, maxStacks: 10), Is.EqualTo(2));
		}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release --filter EvacResolverTest
```

Expected: compilation failure (`HpToOnFireStacks` not defined).

- [ ] **Step 3: Add the function**

Append to `EvacResolver`:

```csharp
		/// <summary>
		/// Map current HP% to onfire stack count. Linear bands below <paramref name="startHealthPct"/>:
		/// at exactly the threshold = 0 stacks, each <paramref name="bandSize"/>% lower adds a stack,
		/// clamped at <paramref name="maxStacks"/>. So with defaults (50, 5, 10): 50%=0, 49%=1, 44%=2, …, 4%=10.
		/// </summary>
		public static int HpToOnFireStacks(int hpPct, int startHealthPct, int bandSize, int maxStacks)
		{
			if (hpPct >= startHealthPct || bandSize <= 0)
				return 0;

			// ceil((startHealthPct - hpPct) / bandSize) without floats.
			var diff = startHealthPct - hpPct;
			var stacks = (diff + bandSize - 1) / bandSize;

			return Math.Clamp(stacks, 0, maxStacks);
		}
```

- [ ] **Step 4: Build and run tests**

```powershell
./make.ps1 all
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release --filter EvacResolverTest
```

Expected: 18 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/EvacResolver.cs engine/OpenRA.Test/OpenRA.Mods.Common/EvacResolverTest.cs
git commit -m "EvacResolver: add HpToOnFireStacks linear-band mapping with tests"
```

---

### Task 4: Create OnFireFromHealth trait

This trait watches the actor's HP each tick. When HP% crosses a band threshold, it grants/revokes external `onfire` condition tokens on itself so the count matches `EvacResolver.HpToOnFireStacks(...)`. Exposes `CurrentStacks` for `VehicleCrew` / `Cargo` to read at eject time.

**Files:**
- Create: `engine/OpenRA.Mods.Common/Traits/OnFireFromHealth.cs`

Reference for the pattern: similar to `GrantConditionOnDamageState.cs` in the same directory but using ITick + ExternalConditions for stackable granting.

- [ ] **Step 1: Create the trait file**

```csharp
// engine/OpenRA.Mods.Common/Traits/OnFireFromHealth.cs
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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grants stackable external onfire condition tokens on self based on HP%.",
		"Stack count = ceil((StartHealthPct - hpPct) / BandSize), clamped to MaxStacks.",
		"Used by vehicles + helicopters so VehicleCrew and Cargo can read the burning state at eject time.")]
	public class OnFireFromHealthInfo : TraitInfo, Requires<IHealthInfo>, Requires<ExternalConditionInfo>
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("External condition to grant (must match an ExternalCondition trait).")]
		public readonly string Condition = "onfire";

		[Desc("HP% at and above which no stacks are granted.")]
		public readonly int StartHealthPct = 50;

		[Desc("HP% per stack below threshold.")]
		public readonly int BandSize = 5;

		[Desc("Maximum stack count.")]
		public readonly int MaxStacks = 10;

		public override object Create(ActorInitializer init) { return new OnFireFromHealth(init.Self, this); }
	}

	public class OnFireFromHealth : ITick, INotifyCreated
	{
		readonly OnFireFromHealthInfo info;
		readonly IHealth health;
		readonly List<int> tokens = new List<int>();

		public int CurrentStacks => tokens.Count;

		public OnFireFromHealth(Actor self, OnFireFromHealthInfo info)
		{
			this.info = info;
			health = self.Trait<IHealth>();
		}

		void INotifyCreated.Created(Actor self) { /* ExternalCondition trait already exists by Required<>. */ }

		void ITick.Tick(Actor self)
		{
			if (self.IsDead || health.MaxHP <= 0)
			{
				ClearAll(self);
				return;
			}

			var hpPct = health.HP * 100 / health.MaxHP;
			var desired = EvacResolver.HpToOnFireStacks(hpPct, info.StartHealthPct, info.BandSize, info.MaxStacks);

			while (tokens.Count < desired)
				GrantOne(self);

			while (tokens.Count > desired)
				RevokeOne(self);
		}

		void GrantOne(Actor self)
		{
			// Find an ExternalCondition trait with matching Condition name; the first one accepts the grant.
			foreach (var ec in self.TraitsImplementing<ExternalCondition>())
			{
				if (ec.Info.Condition != info.Condition)
					continue;

				if (!ec.CanGrantCondition(self))
					return;

				tokens.Add(ec.GrantCondition(self, self));
				return;
			}
		}

		void RevokeOne(Actor self)
		{
			if (tokens.Count == 0)
				return;

			var lastIdx = tokens.Count - 1;
			var token = tokens[lastIdx];
			tokens.RemoveAt(lastIdx);

			foreach (var ec in self.TraitsImplementing<ExternalCondition>())
			{
				if (ec.TryRevokeCondition(self, self, token))
					return;
			}
		}

		void ClearAll(Actor self)
		{
			while (tokens.Count > 0)
				RevokeOne(self);
		}
	}
}
```

- [ ] **Step 2: Verify ExternalCondition API matches**

Quick check: `engine/OpenRA.Mods.Common/Traits/ExternalCondition.cs` should expose `Info.Condition`, `CanGrantCondition(grantor)`, `GrantCondition(self, grantor)`, `TryRevokeCondition(self, grantor, token)`. If method names differ, adjust the trait above to match. Read the file briefly to confirm before building.

```powershell
# Quick read to confirm signatures:
Get-Content engine/OpenRA.Mods.Common/Traits/ExternalCondition.cs | Select-String -Pattern "public (int|bool) (Grant|Revoke|Try|Can)"
```

- [ ] **Step 3: Build engine**

```powershell
./make.ps1 all
```

Expected: build succeeds (no test changes yet — pure trait, manually verified later).

- [ ] **Step 4: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/OnFireFromHealth.cs
git commit -m "OnFireFromHealth: new trait grants onfire stacks based on HP threshold bands"
```

---

### Task 5: Wire OnFireFromHealth + visual overlays into ^Vehicle template

**Files:**
- Modify: `mods/ww3mod/rules/ingame/defaults.yaml` (`^Vehicle` template section)

Pattern reference: `mods/ww3mod/rules/ingame/infantry.yaml` lines 898-980 — same `WithIdleOverlay@Burn_N` + `RequiresCondition: onfire == X || onfire == Y` shape we mirror.

- [ ] **Step 1: Locate the ^Vehicle template**

```powershell
Select-String -Path mods/ww3mod/rules/ingame/defaults.yaml -Pattern "^\^Vehicle:" -Context 0,5
```

Note the line; you'll add traits inside this template block.

- [ ] **Step 2: Add traits to ^Vehicle**

Append the following inside the `^Vehicle:` block (preserving indentation — single tab in this file):

```yaml
	OnFireFromHealth:
		Condition: onfire
		StartHealthPct: 50
		BandSize: 5
		MaxStacks: 10
	ExternalCondition@onfire:
		Condition: onfire
		TotalCap: 10
	WithIdleOverlay@Burn_1:
		RequiresCondition: onfire == 1 || onfire == 2
		Image: infantry-burn-1
		StartSequence: start
		Sequence: loop
		Palette: effect
	WithIdleOverlay@Burn_2:
		RequiresCondition: onfire == 3 || onfire == 4
		Image: infantry-burn-2
		StartSequence: start
		Sequence: loop
		Palette: effect
	WithIdleOverlay@Burn_3:
		RequiresCondition: onfire == 5 || onfire == 6
		Image: infantry-burn-3
		StartSequence: start
		Sequence: loop
		Palette: effect
	WithIdleOverlay@Burn_4:
		RequiresCondition: onfire == 7 || onfire == 8
		Image: infantry-burn-4
		StartSequence: start
		Sequence: loop
		Palette: effect
	WithIdleOverlay@Burn_5:
		RequiresCondition: onfire == 9 || onfire == 10
		Image: infantry-burn-5
		StartSequence: start
		Sequence: loop
		Palette: effect
```

- [ ] **Step 3: Build and YAML-validate**

```powershell
./make.ps1 all
make test
```

Expected: build succeeds, YAML validation passes.

If the launcher boots, manually verify in-game (skirmish): tank takes damage past 50% HP, burn-1 overlay appears; further damage promotes to burn-2/3/4/5. Stack count visible via debug condition viewer if needed.

- [ ] **Step 4: Commit**

```powershell
git add mods/ww3mod/rules/ingame/defaults.yaml
git commit -m "^Vehicle: HP-driven onfire stacks + 5-tier burn visual overlays"
```

---

### Task 6: Wire OnFireFromHealth + visual overlays into ^Aircraft template

**Files:**
- Modify: `mods/ww3mod/rules/ingame/defaults.yaml` (`^Aircraft` template section, or wherever helicopters get their default traits)

- [ ] **Step 1: Locate the helicopter template**

The `^Aircraft` / `^Helicopter` / `^Airborne` templates may live in `defaults.yaml` or in `aircraft.yaml`. Find the right one:

```powershell
Select-String -Path mods/ww3mod/rules/ingame/*.yaml -Pattern "^\^(Helicopter|Aircraft|Airborne):"
```

Add the same six traits (`OnFireFromHealth`, `ExternalCondition@onfire`, `WithIdleOverlay@Burn_1..5`) into the most general airborne template that all helicopters inherit. Use `infantry-burn-N` images for now (placeholder — sprite work is a v1.1 follow-up if scale looks bad).

- [ ] **Step 2: Add the same six trait blocks to the helicopter template**

(Identical to Task 5 step 2 — same trait blocks, just under the helicopter template instead of `^Vehicle`.)

- [ ] **Step 3: Build and YAML-validate**

```powershell
./make.ps1 all
make test
```

Expected: build succeeds, YAML validation passes. In-game: take a Hind below 50% HP, expect smoke overlay.

- [ ] **Step 4: Commit**

```powershell
git add mods/ww3mod/rules/ingame/defaults.yaml mods/ww3mod/rules/ingame/aircraft.yaml
git commit -m "^Helicopter: HP-driven onfire stacks + 5-tier burn visual overlays"
```

---

### Task 7: Refactor VehicleCrew — call EvacResolver, harden stop detection, drop obsolete fields

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/VehicleCrew.cs`

Goal: `VehicleCrew.EjectCrewMember()` and the `INotifyKilled.Killed` path both call `EvacResolver.RollDamage` + `InheritOnFireStacks`. Stop detection requires N consecutive idle ticks. Remove "repaired out of critical → cancel ejecting" branch. Drop `EjectionSurvivalRate`, `CrewDamageThresholdPercent`, `CrewDamageVarianceDivisor`. Add `CrewFireTransferPct` and `StoppedTicksRequired`.

- [ ] **Step 1: Update VehicleCrewInfo fields**

In `VehicleCrew.cs` (Info class), replace the current obsolete fields:

```csharp
		// REMOVE:
		// public readonly int EjectionSurvivalRate = 90;
		// public readonly int CrewDamageThresholdPercent = 25;
		// public readonly int CrewDamageVarianceDivisor = 5;

		// ADD:
		[Desc("Percent of vehicle's current onfire stacks transferred to occupants on staged eject.",
			"100 = full inherit (tanks). 0 = cockpit protected (helicopters). Death-path always grants 10 regardless.")]
		public readonly int CrewFireTransferPct = 100;

		[Desc("Consecutive idle ticks required to count the vehicle as stopped (hardens against single-tick movement-state flicker).")]
		public readonly int StoppedTicksRequired = 8;
```

- [ ] **Step 2: Add stoppedTickCounter field + harden stop detection in Tick**

In the `VehicleCrew` class, add:

```csharp
		[Sync]
		int stoppedTickCounter;
```

Replace the `if (waitingForStop)` block in `ITick.Tick(Actor self)` with:

```csharp
			if (waitingForStop)
			{
				stopWaitCounter++;

				var stopped = mobile == null
					|| (mobile.CurrentMovementTypes & (MovementType.Horizontal | MovementType.Vertical)) == MovementType.None;

				if (stopped)
					stoppedTickCounter++;
				else
					stoppedTickCounter = 0;

				if (stoppedTickCounter >= info.StoppedTicksRequired || stopWaitCounter >= info.StopTimeout)
				{
					waitingForStop = false;
					stoppedTickCounter = 0;
					ejectionCountdown = info.PostStopDelay
						+ self.World.SharedRandom.Next(-info.EjectionDelayVariance, info.EjectionDelayVariance + 1);
				}

				return;
			}
```

- [ ] **Step 3: Refactor EjectCrewMember to use EvacResolver and read OnFireFromHealth**

Add a field to cache the trait at creation time:

```csharp
		OnFireFromHealth onFireFromHealth;
```

In `INotifyCreated.Created`, after `mobile = ...`:

```csharp
			onFireFromHealth = self.TraitOrDefault<OnFireFromHealth>();
```

Replace the entire `EjectCrewMember(string slotName, bool onDeath)` body with a unified version that uses the resolver:

```csharp
		void EjectCrewMember(string slotName, bool onDeath)
		{
			if (!slotIndexByName.TryGetValue(slotName, out var idx))
				return;

			if (!slotOccupied[idx])
				return;

			// Vacate slot + revoke condition.
			slotOccupied[idx] = false;
			if (conditionTokens[idx] != Actor.InvalidConditionToken)
				conditionTokens[idx] = self.RevokeCondition(conditionTokens[idx]);

			if (!info.CrewActors.TryGetValue(slotName, out var actorType))
				return;

			var crewMaxHP = CrewMaxHPFromRules(actorType);
			var jitterPct = self.World.SharedRandom.Next(0, 201); // [0, 200]
			var hpLoss = EvacResolver.RollDamage(finishingDamage, health.MaxHP, crewMaxHP, jitterPct);

			// Dead inside — no actor spawned.
			if (hpLoss >= crewMaxHP)
				return;

			var vehicleStacks = onFireFromHealth?.CurrentStacks ?? 0;
			var inheritedStacks = EvacResolver.InheritOnFireStacks(vehicleStacks, info.CrewFireTransferPct, isDeathPath: onDeath);

			var td = new TypeDictionary
			{
				new OwnerInit(self.Owner),
				new LocationInit(self.Location),
			};

			if (info.TransferVeterancy)
			{
				var ge = self.TraitOrDefault<GainsExperience>();
				if (ge != null && ge.Level > 0)
				{
					var levelXpMap = new[] { 0, 100, 200, 400, 800 };
					var xpToGrant = ge.Level < levelXpMap.Length ? levelXpMap[ge.Level] : levelXpMap[levelXpMap.Length - 1];
					var geInfo = self.Info.TraitInfoOrDefault<GainsExperienceInfo>();
					if (geInfo != null)
						td.Add(new ExperienceInit(geInfo, xpToGrant));
				}
			}

			var damageToApply = hpLoss;
			var stacksToApply = inheritedStacks;

			self.World.AddFrameEndTask(w =>
			{
				var crew = w.CreateActor(actorType, td);
				var positionable = crew.TraitOrDefault<IPositionable>();
				if (positionable != null)
				{
					positionable.SetPosition(crew, self.Location);
					if (!positionable.CanEnterCell(self.Location, crew, BlockedByActor.None))
					{
						var placed = false;
						foreach (var cell in w.Map.FindTilesInAnnulus(self.Location, 1, 2))
						{
							if (positionable.CanEnterCell(cell, crew, BlockedByActor.None))
							{
								positionable.SetPosition(crew, cell);
								placed = true;
								break;
							}
						}

						if (!placed)
							crew.Kill(crew);
					}
				}

				var nbms = crew.TraitsImplementing<INotifyBlockingMove>();
				foreach (var nbm in nbms)
					nbm.OnNotifyBlockingMove(crew, crew);

				if (damageToApply > 0 && !crew.IsDead)
					crew.InflictDamage(self, new Damage(damageToApply));

				// Apply inherited onfire stacks.
				if (stacksToApply > 0 && !crew.IsDead)
				{
					var ec = crew.TraitsImplementing<ExternalCondition>().FirstOrDefault(e => e.Info.Condition == "onfire");
					if (ec != null)
					{
						for (var i = 0; i < stacksToApply; i++)
						{
							if (!ec.CanGrantCondition(self))
								break;
							ec.GrantCondition(crew, self);
						}
					}
				}
			});
		}
```

- [ ] **Step 4: Remove the "repaired out of critical → cancel" branch**

In `INotifyDamageStateChanged.DamageStateChanged`, delete the `else if (e.DamageState < info.EjectionDamageState && e.PreviousDamageState >= info.EjectionDamageState)` block entirely. Once ejection starts, it runs to completion.

- [ ] **Step 5: Remove EjectionSurvivalRate-based onDeath path**

The previous code in `INotifyKilled.Killed` did `self.World.SharedRandom.Next(100) >= info.EjectionSurvivalRate` for survival. Since the resolver now handles all damage, just iterate and call `EjectCrewMember(slotName, onDeath: true)` for each occupied slot — the resolver does the rest:

```csharp
		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (SuppressEjection)
			{
				ejecting = false;
				return;
			}

			// Capture the killing-shot damage as finishingDamage if we hadn't already entered critical.
			if (finishingDamage == 0)
				finishingDamage = e.Damage?.Value ?? 0;

			foreach (var slotName in ejectionOrder)
			{
				if (!slotIndexByName.TryGetValue(slotName, out var idx))
					continue;

				if (slotOccupied[idx])
					EjectCrewMember(slotName, onDeath: true);
			}

			ejecting = false;
		}
```

- [ ] **Step 6: Build engine**

```powershell
./make.ps1 all
```

Expected: build succeeds. Compile errors here mean the resolver API doesn't match — re-read Tasks 1-3 to align signatures.

- [ ] **Step 7: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/VehicleCrew.cs
git commit -m "VehicleCrew: route through EvacResolver, harden stop detection, drop obsolete fields"
```

---

### Task 8: Update vehicle YAML to drop obsolete fields and add new ones

**Files:**
- Modify: `mods/ww3mod/rules/ingame/vehicles-america.yaml`
- Modify: `mods/ww3mod/rules/ingame/vehicles-russia.yaml`
- Possibly: `mods/ww3mod/rules/ingame/defaults.yaml` (if `VehicleCrew` defaults live there)

- [ ] **Step 1: Find every VehicleCrew block**

```powershell
Select-String -Path mods/ww3mod/rules/ingame/*.yaml -Pattern "VehicleCrew:" -List
```

For each match, open the file and locate the trait. Each `VehicleCrew` block needs:

- **Remove:** `EjectionSurvivalRate`, `CrewDamageThresholdPercent`, `CrewDamageVarianceDivisor` lines (if present).
- **Add:** `CrewFireTransferPct: 100` and `StoppedTicksRequired: 8` (defaults are fine for tanks; you can omit these lines since they match the C# defaults, but adding explicitly documents intent).

- [ ] **Step 2: Make the edits across all vehicle YAML files**

For each `VehicleCrew:` block in `vehicles-america.yaml` and `vehicles-russia.yaml`:

```yaml
	VehicleCrew:
		# ... existing CrewSlots / CrewActors / SlotConditions / EjectionDelay / etc kept as-is ...
		CrewFireTransferPct: 100        # ADD (or omit — matches default)
		StoppedTicksRequired: 8         # ADD (or omit — matches default)
		# REMOVE these lines if present:
		# EjectionSurvivalRate: 90
		# CrewDamageThresholdPercent: 25
		# CrewDamageVarianceDivisor: 5
```

**Tip:** since the defaults match, the cleanest minimal edit is just deleting the obsolete fields. Add `CrewFireTransferPct` only on the helicopter template (Task 10).

- [ ] **Step 3: Build and YAML-validate**

```powershell
./make.ps1 all
make test
```

Expected: build + YAML validation succeeds. If validation flags "unknown field," check that the C# field matches what's in YAML (case-sensitive).

- [ ] **Step 4: Commit**

```powershell
git add mods/ww3mod/rules/ingame/vehicles-america.yaml mods/ww3mod/rules/ingame/vehicles-russia.yaml mods/ww3mod/rules/ingame/defaults.yaml
git commit -m "Vehicle YAML: drop obsolete VehicleCrew fields (now handled by EvacResolver)"
```

---

### Task 9: Add critical-state staged eject path to Cargo

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/Cargo.cs`

Goal: Cargo gets the same critical-state staged ejection path as `VehicleCrew`. New fields: `EjectOnCritical`, `EjectionDelay`, `EjectionDelayVariance`, `PostStopDelay`, `StopTimeout`, `StoppedTicksRequired`. Implements `INotifyDamageStateChanged` + `ITick`. Existing `INotifyKilled.Killed` becomes the death-path fallback that uses the resolver too.

- [ ] **Step 1: Add Info fields**

In `CargoInfo`:

```csharp
		[Desc("Eject passengers when transport hits critical damage state (staged, one at a time).")]
		public readonly bool EjectOnCritical = false;

		[Desc("Damage state that triggers staged passenger ejection.")]
		public readonly DamageState EjectionDamageState = DamageState.Critical;

		[Desc("Base ticks between each passenger ejection during staged path.")]
		public readonly int EjectionDelay = 12;

		[Desc("Random ± variance added to ejection delay.")]
		public readonly int EjectionDelayVariance = 4;

		[Desc("Additional delay (ticks) after the transport comes to a full stop before the first passenger ejects.")]
		public readonly int PostStopDelay = 38;

		[Desc("Maximum ticks to wait for the transport to stop after entering critical. Eject anyway after this.")]
		public readonly int StopTimeout = 150;

		[Desc("Consecutive idle ticks required to count as stopped.")]
		public readonly int StoppedTicksRequired = 8;

		[Desc("Percent of vehicle's current onfire stacks transferred to passengers on staged eject (0..100). Death-path always grants 10.")]
		public readonly int CrewFireTransferPct = 100;
```

- [ ] **Step 2: Update Cargo class declaration to implement INotifyDamageStateChanged + ITick**

```csharp
	public class Cargo : IIssueOrder, IResolveOrder, IOrderVoice, INotifyCreated, INotifyKilled, INotifyDamage,
		INotifyOwnerChanged, INotifySold, INotifyActorDisposing, IIssueDeployOrder,
		ITransformActorInitModifier, INotifyDamageStateChanged, ITick
	{
```

- [ ] **Step 3: Add staged-eject state fields and trait references**

Inside `Cargo` class, near other private fields:

```csharp
		Mobile mobile;
		OnFireFromHealth onFireFromHealth;
		IHealth health;

		int finishingDamage;
		[Sync] bool waitingForStop;
		[Sync] int stopWaitCounter;
		[Sync] int stoppedTickCounter;
		[Sync] int ejectionCountdown;
		[Sync] bool ejectingStaged;

		/// <summary>Set by HeliEmergencyLanding during descent — pauses staged ejection until landing.</summary>
		public bool SuppressEjection { get; set; }
```

In `INotifyCreated.Created`, after existing setup, cache the traits:

```csharp
			mobile = self.TraitOrDefault<Mobile>();
			onFireFromHealth = self.TraitOrDefault<OnFireFromHealth>();
			health = self.Trait<IHealth>();
```

- [ ] **Step 4: Implement INotifyDamageStateChanged**

Add to `Cargo`:

```csharp
		void INotifyDamageStateChanged.DamageStateChanged(Actor self, AttackInfo e)
		{
			if (!Info.EjectOnCritical)
				return;

			if (e.DamageState >= Info.EjectionDamageState && e.PreviousDamageState < Info.EjectionDamageState)
			{
				if (ejectingStaged || IsEmpty())
					return;

				ejectingStaged = true;
				finishingDamage = e.Damage?.Value ?? 0;

				if (mobile != null)
				{
					waitingForStop = true;
					stopWaitCounter = 0;
					stoppedTickCounter = 0;
				}
				else
				{
					waitingForStop = false;
					ejectionCountdown = Info.EjectionDelay
						+ self.World.SharedRandom.Next(-Info.EjectionDelayVariance, Info.EjectionDelayVariance + 1);
				}
			}
		}
```

- [ ] **Step 5: Implement ITick**

```csharp
		void ITick.Tick(Actor self)
		{
			if (!ejectingStaged || self.IsDead || SuppressEjection)
				return;

			if (waitingForStop)
			{
				stopWaitCounter++;

				var stopped = mobile == null
					|| (mobile.CurrentMovementTypes & (MovementType.Horizontal | MovementType.Vertical)) == MovementType.None;

				if (stopped)
					stoppedTickCounter++;
				else
					stoppedTickCounter = 0;

				if (stoppedTickCounter >= Info.StoppedTicksRequired || stopWaitCounter >= Info.StopTimeout)
				{
					waitingForStop = false;
					stoppedTickCounter = 0;
					ejectionCountdown = Info.PostStopDelay
						+ self.World.SharedRandom.Next(-Info.EjectionDelayVariance, Info.EjectionDelayVariance + 1);
				}

				return;
			}

			if (--ejectionCountdown > 0)
				return;

			if (IsEmpty())
			{
				ejectingStaged = false;
				return;
			}

			EjectOnePassengerStaged(self);
			ejectionCountdown = Info.EjectionDelay
				+ self.World.SharedRandom.Next(-Info.EjectionDelayVariance, Info.EjectionDelayVariance + 1);
		}

		void EjectOnePassengerStaged(Actor self)
		{
			if (!CanUnload(BlockedByActor.All))
				return;

			var passenger = Unload(self);
			ApplyEvacOutcomeToPassenger(self, passenger, isDeathPath: false);
			PlacePassengerInWorld(self, passenger);
		}

		void ApplyEvacOutcomeToPassenger(Actor self, Actor passenger, bool isDeathPath)
		{
			var passengerHealth = passenger.TraitOrDefault<IHealth>();
			if (passengerHealth == null)
				return;

			var jitterPct = self.World.SharedRandom.Next(0, 201);
			var hpLoss = EvacResolver.RollDamage(finishingDamage, health.MaxHP, passengerHealth.MaxHP, jitterPct);
			var vehicleStacks = onFireFromHealth?.CurrentStacks ?? 0;
			var stacks = EvacResolver.InheritOnFireStacks(vehicleStacks, Info.CrewFireTransferPct, isDeathPath);

			if (hpLoss >= passengerHealth.MaxHP)
			{
				// Dead inside — destroy the actor without spawning into the world.
				passenger.Kill(self);
				return;
			}

			if (hpLoss > 0)
				passenger.InflictDamage(self, new Damage(hpLoss));

			if (stacks > 0 && !passenger.IsDead)
			{
				var ec = passenger.TraitsImplementing<ExternalCondition>().FirstOrDefault(e => e.Info.Condition == "onfire");
				if (ec != null)
				{
					for (var i = 0; i < stacks; i++)
					{
						if (!ec.CanGrantCondition(self))
							break;
						ec.GrantCondition(passenger, self);
					}
				}
			}
		}

		void PlacePassengerInWorld(Actor self, Actor passenger)
		{
			if (passenger.IsDead)
				return;

			var cp = self.CenterPosition;
			var inAir = self.World.Map.DistanceAboveTerrain(cp).Length != 0;
			var positionable = passenger.Trait<IPositionable>();
			positionable.SetPosition(passenger, self.Location);

			if (!inAir && positionable.CanEnterCell(self.Location, self, BlockedByActor.None))
			{
				self.World.AddFrameEndTask(w => w.Add(passenger));
				var nbms = passenger.TraitsImplementing<INotifyBlockingMove>();
				foreach (var nbm in nbms)
					nbm.OnNotifyBlockingMove(passenger, passenger);
			}
			else
			{
				passenger.Kill(self);
			}
		}
```

- [ ] **Step 6: Refactor INotifyKilled.Killed to route through resolver**

Replace the existing `Killed` body:

```csharp
		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (Info.EjectOnDeath)
			{
				if (finishingDamage == 0)
					finishingDamage = e.Damage?.Value ?? 0;

				while (!IsEmpty() && CanUnload(BlockedByActor.All))
				{
					var passenger = Unload(self);
					ApplyEvacOutcomeToPassenger(self, passenger, isDeathPath: true);
					PlacePassengerInWorld(self, passenger);
				}
			}
			else
			{
				foreach (var c in cargo)
					c.Kill(e.Attacker);

				cargo.Clear();
			}

			ejectingStaged = false;
		}
```

- [ ] **Step 7: Verify `using System.Linq;` is present**

The new code uses `.FirstOrDefault(...)`. If not already imported at the top of `Cargo.cs`, add:

```csharp
using System.Linq;
```

- [ ] **Step 8: Build**

```powershell
./make.ps1 all
```

Expected: build succeeds.

- [ ] **Step 9: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/Cargo.cs
git commit -m "Cargo: add critical-state staged eject path via EvacResolver (passenger unification)"
```

---

### Task 10: Wire EjectOnCritical and per-heli CrewFireTransferPct in YAML

**Files:**
- Modify: `mods/ww3mod/rules/ingame/aircraft.yaml` (`^Helicopter` template — Cargo block + VehicleCrew block)
- Modify: any transport vehicle YAML with `Cargo:` (APCs/IFVs in `vehicles-{america,russia}.yaml`)

- [ ] **Step 1: Helicopter template**

In the `^Helicopter:` block (or whichever is the most common helicopter template), update the `VehicleCrew` and `Cargo` blocks:

```yaml
	VehicleCrew:
		# ... existing CrewSlots / CrewActors / SlotConditions / EjectionDelay etc kept ...
		CrewFireTransferPct: 0          # NEW — cockpit protects on staged eject

	Cargo:
		# ... existing fields kept ...
		EjectOnDeath: True              # already present, kept
		EjectOnCritical: True           # NEW
		EjectionDelay: 12               # NEW
		EjectionDelayVariance: 4        # NEW
		PostStopDelay: 38               # NEW
		StopTimeout: 150                # NEW
		StoppedTicksRequired: 8         # NEW
		CrewFireTransferPct: 0          # NEW — passengers cockpit-protected on staged
```

- [ ] **Step 2: Ground transport templates**

For each transport (APC, IFV, etc.) with a `Cargo:` block, add:

```yaml
	Cargo:
		# ... existing fields ...
		EjectOnCritical: True           # NEW
		# Other staged-path fields default OK; or set explicitly:
		# EjectionDelay: 12
		# EjectionDelayVariance: 4
		# PostStopDelay: 38
		# StoppedTicksRequired: 8
		# CrewFireTransferPct: 100  (default; passengers fully inherit from burning APC)
```

- [ ] **Step 3: Build and YAML-validate**

```powershell
./make.ps1 all
make test
```

- [ ] **Step 4: Commit**

```powershell
git add mods/ww3mod/rules/ingame/aircraft.yaml mods/ww3mod/rules/ingame/vehicles-america.yaml mods/ww3mod/rules/ingame/vehicles-russia.yaml
git commit -m "Cargo YAML: enable EjectOnCritical on helis + ground transports; helis CrewFireTransferPct=0"
```

---

### Task 11: Update HeliEmergencyLanding — remove heavy path, simplify safe landing

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs`

Goal: only `Critical` triggers the controlled descent. Safe landing no longer calls `EjectAllCrew()` / transfers to neutral / sets `AllowForeignCrew` / activates `RepairableBuilding`. After landing, the heli sits and burns — `VehicleCrew` and `Cargo` run their critical-state staged paths naturally once `SuppressEjection` is revoked.

- [ ] **Step 1: Remove the heavy-damage branch in DamageStateChanged**

In `INotifyDamageStateChanged.DamageStateChanged`, delete these blocks:

```csharp
			// DELETE — heavy = autorotation safe-land path is gone.
			if (State == EmergencyState.None && e.DamageState >= info.AutorotationDamageState
				&& e.DamageState < info.CrashDamageState && !self.IsAtGroundLevel())
			{
				StartAutorotation(self);
				return;
			}

			// DELETE — escalate is irrelevant once heavy path is gone.
			if (State == EmergencyState.Autorotation && e.DamageState >= info.CrashDamageState)
			{
				TransitionToCrash(self);
				return;
			}

			// DELETE — repaired-out-of-heavy cancel is irrelevant.
			if (State == EmergencyState.Autorotation && e.DamageState < info.AutorotationDamageState)
			{
				CancelAutorotation(self);
				return;
			}
```

The remaining trigger is just:

```csharp
			// Start controlled descent on critical (only if airborne).
			if (State == EmergencyState.None && e.DamageState >= info.CrashDamageState && !self.IsAtGroundLevel())
			{
				StartCrashDescent(self);
				return;
			}

			CheckDisabledRecovery(self);
```

Rename `StartCrash` → `StartCrashDescent` for clarity (since this is now the only path and represents the controlled descent, which then naturally resolves to spinning crash if killed mid-glide).

- [ ] **Step 2: StartCrashDescent uses HeliAutorotate**

Today `StartCrash` queues `HeliCrashLand`. Switch it to `HeliAutorotate` (the controlled-descent activity) — that's the new model:

```csharp
		void StartCrashDescent(Actor self)
		{
			State = EmergencyState.Crashing;

			if (crashLandingToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.CrashLandingCondition))
				crashLandingToken = self.GrantCondition(info.CrashLandingCondition);

			if (suppressEjectToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.SuppressEjectCondition))
				suppressEjectToken = self.GrantCondition(info.SuppressEjectCondition);

			if (vehicleCrew != null)
				vehicleCrew.SuppressEjection = true;

			if (cargo != null)
				cargo.SuppressEjection = true;

			self.CancelActivity();

			var speed = aircraft.Info.Speed * info.AutorotationSpeedPercent / 100;
			self.QueueActivity(false, new HeliAutorotate(self, this, info, aircraft, speed));
		}
```

Delete `StartAutorotation`, `TransitionToCrash`, and `CancelAutorotation` methods entirely.

- [ ] **Step 3: Simplify OnSafeLanding**

Replace `OnSafeLanding` with the new minimal version (no eject-all, no neutral transfer, no AllowForeignCrew, no RepairableBuilding activation):

```csharp
		public void OnSafeLanding(Actor self)
		{
			// Revoke flight conditions.
			if (autorotationToken != Actor.InvalidConditionToken)
				autorotationToken = self.RevokeCondition(autorotationToken);

			// Revoke suppress-eject — VehicleCrew + Cargo critical-state paths take over.
			if (suppressEjectToken != Actor.InvalidConditionToken)
				suppressEjectToken = self.RevokeCondition(suppressEjectToken);

			if (vehicleCrew != null)
				vehicleCrew.SuppressEjection = false;

			if (cargo != null)
				cargo.SuppressEjection = false;

			State = EmergencyState.None;

			// Disabled on ground; existing critical DOT continues to drain HP toward 0.
			if (disabledToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.DisabledCondition))
				disabledToken = self.GrantCondition(info.DisabledCondition);

			aircraft.CurrentVelocity = WVec.Zero;
		}
```

- [ ] **Step 4: Implement unsafe-landing damage**

Replace `OnUnsafeLanding` to apply 30% remaining HP damage instead of outright killing:

```csharp
		public void OnUnsafeLanding(Actor self)
		{
			var hp = self.Trait<IHealth>();
			var damageOnImpact = hp.HP * 30 / 100;
			if (damageOnImpact > 0)
				self.InflictDamage(self, new Damage(damageOnImpact));

			// If the impact didn't kill it, proceed as if it landed safely (heli sits + burns).
			if (!self.IsDead)
				OnSafeLanding(self);
		}
```

- [ ] **Step 5: Remove neutral-transfer + capture plumbing**

Delete in `OnSafeLanding` (now done in step 3) and remove the YAML field handler — the field stays in YAML for now but is unused; we'll remove it from YAML in a later task.

Also delete this method which is no longer called:

```csharp
		// DELETE EjectAllPassengers — Cargo handles its own critical-state path.
		void EjectAllPassengers(Actor self) { ... }
```

And update `INotifyCreated.Created` to keep caching `cargo` (still used by SuppressEjection) but drop the `LoadingBlocked` calls if those were tied to the old re-entry capture path. Search the file for `LoadingBlocked` and remove any sets that were related to capture mode.

- [ ] **Step 6: Build**

```powershell
./make.ps1 all
```

Expected: build succeeds. If it doesn't compile, fix references to deleted methods (`EjectAllCrew`, `EjectAllPassengers`, `AllowForeignCrew`, `TransferToNeutralOnSafeLanding`).

- [ ] **Step 7: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs
git commit -m "HeliEmergencyLanding: drop heavy path, simplify safe landing, route to staged eject"
```

---

### Task 12: Add mid-glide death rules to HeliEmergencyLanding

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs`

Goal: when the heli is killed mid-air during descent, decide between spinning crash (existing `HeliCrashLand`) and rotor-blown-fall (existing `Falls`) based on `SpinsOnCrash` + killing-shot magnitude vs `RotorDestroyDamageThresholdPct`.

- [ ] **Step 1: Add Info fields**

In `HeliEmergencyLandingInfo`:

```csharp
		[Desc("If a single killing shot is at least this percent of MaxHP, skip spinning and just fall (rotors destroyed).",
			"Default 50: a finishing shot >= 50% MaxHP that kills the heli mid-air blows the rotors off.")]
		public readonly int RotorDestroyDamageThresholdPct = 50;

		[GrantedConditionReference]
		[Desc("Condition granted when rotors are destroyed (used to hide rotor sprite during fall).")]
		public readonly string RotorDestroyedCondition = "rotor-destroyed";
```

- [ ] **Step 2: Track the rotor-destroyed token**

Add field at top of `HeliEmergencyLanding` class:

```csharp
		int rotorDestroyedToken = Actor.InvalidConditionToken;
```

- [ ] **Step 3: Replace INotifyKilled.Killed body**

Replace the placeholder comment with active logic:

```csharp
		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (self.IsAtGroundLevel())
				return;

			var killingDamage = e.Damage?.Value ?? 0;
			var maxHP = self.Trait<IHealth>().MaxHP;
			var thresholdHP = maxHP * info.RotorDestroyDamageThresholdPct / 100;

			var fallNoSpin = !info.SpinsOnCrash || killingDamage >= thresholdHP;

			if (fallNoSpin && rotorDestroyedToken == Actor.InvalidConditionToken
				&& !string.IsNullOrEmpty(info.RotorDestroyedCondition))
				rotorDestroyedToken = self.GrantCondition(info.RotorDestroyedCondition);

			// SuppressEjection stays active from descent — crew/passengers die with the heli.
			// HeliAutorotate / HeliCrashLand will handle the actual fall animation; we just
			// flag the condition so sprite-hiding takes effect.
		}
```

Note: depending on how the existing `HeliAutorotate` activity handles being killed mid-flight vs `HeliCrashLand`, you may need to also queue an explicit `Falls`/`HeliCrashLand` activity here. Read those activities (`engine/OpenRA.Mods.Common/Activities/Air/HeliAutorotate.cs`, `HeliCrashLand.cs`) to confirm whether the existing `Falls` activity is what runs at HP=0, or if you need to cancel + queue manually. If manual queueing is needed, add:

```csharp
			self.CancelActivity();
			self.QueueActivity(false, fallNoSpin
				? (Activity)new Falls(self, ...)
				: new HeliCrashLand(self, this, info, aircraft));
```

(Match the existing constructor signatures of `Falls` and `HeliCrashLand` in the code.)

- [ ] **Step 4: Build**

```powershell
./make.ps1 all
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs
git commit -m "HeliEmergencyLanding: mid-glide death rules — fall vs spin via RotorDestroyDamageThresholdPct"
```

---

### Task 13: Wire RotorDestroyDamageThresholdPct + rotor-hide overlay in heli YAML

**Files:**
- Modify: `mods/ww3mod/rules/ingame/aircraft.yaml` (`^Helicopter` template)

- [ ] **Step 1: Add HeliEmergencyLanding YAML fields**

In `^Helicopter:`, update the `HeliEmergencyLanding:` block:

```yaml
	HeliEmergencyLanding:
		# ... existing fields kept ...
		RotorDestroyDamageThresholdPct: 50          # NEW
		RotorDestroyedCondition: rotor-destroyed    # NEW
		# REMOVE if present:
		# AutorotationDamageState: Heavy            (heavy path is gone)
		# TransferToNeutralOnSafeLanding: True
```

- [ ] **Step 2: Gate rotor sprite on `!rotor-destroyed`**

For each rotor `WithIdleOverlay` / `WithSpriteRotor` / equivalent on each helicopter actor, add `RequiresCondition: !rotor-destroyed`. Find them with:

```powershell
Select-String -Path mods/ww3mod/rules/ingame/aircraft*.yaml -Pattern "Rotor" -Context 0,5
```

Add `RequiresCondition: !rotor-destroyed` to each rotor render trait.

- [ ] **Step 3: Confirm SpinsOnCrash on dual-rotor helis**

```powershell
Select-String -Path mods/ww3mod/rules/ingame/aircraft*.yaml -Pattern "SpinsOnCrash"
```

Confirm Chinook (CHIN/HALO) have `SpinsOnCrash: False`. Single-rotor helis (HELI, HIND, MI28, etc.) leave default `True`.

- [ ] **Step 4: Build and YAML-validate**

```powershell
./make.ps1 all
make test
```

- [ ] **Step 5: Commit**

```powershell
git add mods/ww3mod/rules/ingame/aircraft.yaml mods/ww3mod/rules/ingame/aircraft-america.yaml mods/ww3mod/rules/ingame/aircraft-russia.yaml
git commit -m "Heli YAML: RotorDestroyDamageThresholdPct + gate rotor sprite on !rotor-destroyed"
```

---

### Task 14: Gate Repairable on !critical-damage in ^Vehicle / ^Aircraft templates

**Files:**
- Modify: `mods/ww3mod/rules/ingame/defaults.yaml` (`^Vehicle` and `^Aircraft` templates)

Goal: critical-damaged vehicles can't be saved by engineers. Reuse the existing `critical-damage` condition (granted by `GrantConditionOnDamageState`).

- [ ] **Step 1: Verify critical-damage condition is granted on `^Vehicle` / `^Aircraft`**

```powershell
Select-String -Path mods/ww3mod/rules/ingame/*.yaml -Pattern "critical-damage" -Context 0,3
```

Confirm something like `GrantConditionOnDamageState: { Condition: critical-damage, ValidDamageStates: Critical }` already lives in `^ExistsInWorld` / `^Vehicle` / `^Aircraft`. If not, add to defaults.yaml:

```yaml
	GrantConditionOnDamageState@critical-damage:
		Condition: critical-damage
		ValidDamageStates: Critical
```

- [ ] **Step 2: Gate Repairable / RepairableNear / RepairableBuilding**

For every existing `Repairable:`, `RepairableNear:`, and `RepairableBuilding:` trait that vehicles + helicopters inherit, add:

```yaml
		RequiresCondition: !critical-damage
```

If a trait already has `RequiresCondition: X`, change to `RequiresCondition: X && !critical-damage`.

- [ ] **Step 3: Build and YAML-validate**

```powershell
./make.ps1 all
make test
```

- [ ] **Step 4: Commit**

```powershell
git add mods/ww3mod/rules/ingame/defaults.yaml
git commit -m "Defaults: gate Repairable on !critical-damage — burning vehicles unrecoverable"
```

---

### Task 15: Update crew.yaml — SMG, ammo, role costs, selection priority/class

**Files:**
- Modify: `mods/ww3mod/rules/ingame/crew.yaml` (or wherever `^CrewMember` template + role actors live)

- [ ] **Step 1: Find the template + role actors**

```powershell
Select-String -Path mods/ww3mod/rules/ingame/*.yaml -Pattern "^crew\." -Context 0,5
Select-String -Path mods/ww3mod/rules/ingame/*.yaml -Pattern "^\^CrewMember:" -Context 0,5
```

- [ ] **Step 2: Update ^CrewMember template**

```yaml
^CrewMember:
	Tooltip:
		Name: Crew Survivor
	Selectable:
		Priority: 5                       # lower than infantry default 10
		Class: CrewSurvivor               # distinct class for per-class filter
	Valued:
		Cost: 100                         # baseline (Driver/Gunner/Copilot)
	Armament@1:
		Weapon: SMG                       # was Pistol — confirm SMG weapon exists in rules/weapons/*.yaml
	AmmoPool@1:
		Ammo: 10                          # ~1/3 of regular SMG infantry's 30
```

Confirm `SMG` weapon exists: `Select-String -Path mods/ww3mod/rules/weapons/*.yaml -Pattern "^SMG:"`. If not, copy the weapon from regular SMG infantry.

- [ ] **Step 3: Update per-role overrides**

```yaml
crew.driver.america:
	Inherits: ^CrewMember
	# Cost: 100 from baseline

crew.gunner.america:
	Inherits: ^CrewMember
	# Cost: 100 from baseline

crew.commander.america:
	Inherits: ^CrewMember
	Valued:
		Cost: 200

crew.pilot.america:
	Inherits: ^CrewMember
	Valued:
		Cost: 300

crew.copilot.america:
	Inherits: ^CrewMember
	Valued:
		Cost: 200
```

Mirror for Russia faction.

- [ ] **Step 4: Set default stances**

The existing `UnitDefaultsManager` reads from `Platform.SupportDir/ww3mod/unit-defaults.yaml`. Add (or document for the user to add) defaults for each crew actor type:

- `Resupply: Evacuate`
- `Engagement: Defensive`
- `Fire: FireAtWill`

If this is set per-actor in YAML rules instead, find the existing pattern (search for `DefaultStance` or similar) and apply it to `^CrewMember`.

- [ ] **Step 5: Build and YAML-validate**

```powershell
./make.ps1 all
make test
```

- [ ] **Step 6: Commit**

```powershell
git add mods/ww3mod/rules/ingame/crew.yaml
git commit -m "Crew: SMG + 1/3 ammo + role-based costs (D/G=100, Cmdr=200, Pilot=300, Copilot=200)"
```

---

### Task 16: Delete CrewMember.cs + EnterAsCrew.cs + UI/order plumbing

**Files:**
- Delete: `engine/OpenRA.Mods.Common/Traits/CrewMember.cs`
- Delete: `engine/OpenRA.Mods.Common/Activities/Air/EnterAsCrew.cs`
- Modify: any UI/order/cursor plumbing referencing crew re-entry

- [ ] **Step 1: Find references to CrewMember trait + EnterAsCrew activity**

```powershell
Select-String -Path engine/**/*.cs -Pattern "CrewMember[^I]|EnterAsCrew" -List
```

- [ ] **Step 2: Delete the two files**

```powershell
git rm engine/OpenRA.Mods.Common/Traits/CrewMember.cs engine/OpenRA.Mods.Common/Activities/Air/EnterAsCrew.cs
```

- [ ] **Step 3: Clean up references**

For each file flagged in step 1, remove the import + usage. Common patterns:

- Order generators that issued `EnterCrew` orders → delete the order generator entirely.
- Cursor providers showing "enter as crew" cursor → remove that branch.
- VehicleCrew's `FillSlot`, `ReserveSlot`, `UnreserveSlot`, `CanAcceptRole`, `HasEmptySlot` public API → delete (no callers after CrewMember is gone).
- Any YAML actor referencing `CrewMember:` trait → remove the trait line.

- [ ] **Step 4: Search YAML for CrewMember trait usage**

```powershell
Select-String -Path mods/ww3mod/rules/**/*.yaml -Pattern "^\s+CrewMember:" -List
```

Remove each match.

- [ ] **Step 5: Build**

```powershell
./make.ps1 all
```

Expected: build succeeds. Compile errors guide cleanup of remaining references.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "Remove crew re-entry: delete CrewMember trait + EnterAsCrew activity + UI plumbing"
```

---

### Task 17: Clean up dead code in VehicleCrew + HeliEmergencyLanding

**Files:**
- Modify: `engine/OpenRA.Mods.Common/Traits/VehicleCrew.cs`
- Modify: `engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs`

After Task 16, several public methods in VehicleCrew/HeliEmergencyLanding are dead. Delete them.

- [ ] **Step 1: VehicleCrew dead-code removal**

Delete these from `VehicleCrew.cs`:

- `EjectAllCrew()` method (was used by HeliEmergencyLanding.OnSafeLanding, now removed).
- `VacateSlot(string role)` (only Crew re-entry used it).
- `AllowForeignCrew` property (capture-by-pilot path is gone).
- `slotReserved` array + `ReserveSlot/UnreserveSlot/CanAcceptRole/HasEmptySlot/FillSlot/IsSlotOccupied/EmptySlots` (re-entry support).

- [ ] **Step 2: HeliEmergencyLanding dead-code removal**

Delete from `HeliEmergencyLanding.cs`:

- `TransferToNeutralOnSafeLanding` field (Info class).
- `using System.Linq;` if no longer used (or if `Players.FirstOrDefault` is the only use).
- The `cargo.LoadingBlocked = true` line in `OnSafeLanding` — no longer relevant since re-entry is gone. Also remove `cargo.LoadingBlocked = false` in `CheckDisabledRecovery` if it exists.
- The `vehicleCrew.AllowForeignCrew = true` line in `OnSafeLanding`.

- [ ] **Step 3: Build**

```powershell
./make.ps1 all
```

- [ ] **Step 4: Commit**

```powershell
git add engine/OpenRA.Mods.Common/Traits/VehicleCrew.cs engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs
git commit -m "Remove dead code from VehicleCrew + HeliEmergencyLanding after re-entry removal"
```

---

### Task 18: Integration playtest + tuning

**Files:** none (manual verification + tuning)

This is a manual verification pass. Run `./make.ps1 all` then `./launch-game.cmd` and run through the checklist. Tune values where outcomes feel off; commit YAML adjustments per finding.

- [ ] **Step 1: Build a clean release**

```powershell
./make.ps1 all
```

- [ ] **Step 2: Tank one-shot scenarios**

Skirmish on River Zeta WW3. Have a player tank ATGM-shot the AI tank.

- Expected: 0–1 crew survive, 0–1 are engulfed (onfire=10), most die inside.
- Verify: vehicle shows progressively higher burn-N overlay as it takes damage past 50%.

- [ ] **Step 3: Tank grind scenarios**

Have small-arms fire (rifle squad) drain a tank slowly to death.

- Expected: 2–3 crew survive, mostly clean to lightly burned.
- Verify: crew survivors walk toward map edge and refund cashback.

- [ ] **Step 4: APC/IFV staged eject**

Damage a transport with passengers to ~50% HP.

- Expected: passengers start disgorging staggered ~0.4s apart, even while transport is alive.
- Verify: transport that survives below 50% sits damaged with empty cargo.

- [ ] **Step 5: Helicopter critical descent on safe terrain**

Hit a helicopter with a missile to push it past 50%.

- Expected: heli enters controlled descent, player can right-click to steer, lands on safe terrain.
- After landing: heli sits, burn overlays intensify as HP drops via critical DOT, pilots eject one-by-one.
- Pilots emerge **clean** (no onfire) — cockpit-protected.

- [ ] **Step 6: Helicopter explodes mid-eject**

Same scenario but keep firing on the heli after it lands.

- Expected: remaining pilots emerge **engulfed** (onfire=10) + heavily damaged on death.

- [ ] **Step 7: Helicopter mid-glide death — fall vs spin**

Single big missile (≥50% MaxHP) on a flying heli at low health: should fall, no spin, rotor sprite hidden.
Smaller round trickle that kills mid-glide: should spin and crash.
Chinook killed any way: should fall, no spin (SpinsOnCrash: False).

- [ ] **Step 8: Re-entry verification**

With a crew survivor selected, click on a damaged tank: cursor should NOT show "enter as crew." Order should be rejected.

- [ ] **Step 9: No engineer-saving**

Damage a tank to <50% HP, target with engineer: cursor should NOT show "repair." Repair refused.

- [ ] **Step 10: Crew survivor selection**

Box-select an army containing both regular infantry and crew survivors. Per-class filter (default keyboard shortcut for "select infantry") should separate them.

- [ ] **Step 11: Tune and commit**

If pacing or lethality feels off, adjust the constants in YAML / `EvacResolver.CrewLethalityScale`. Each tuning round = one commit.

```powershell
git commit -m "Tune crew evac: <what changed and why>"
```

- [ ] **Step 12: Final FINALIZE**

Run the FINALIZE workflow per `CLAUDE.md`:

1. Bell: `printf "\a"`.
2. Update `CLAUDE/RELEASE_V1.md` — flip statuses for crew evac items.
3. Update `CLAUDE/HOTBOARD.md` — refresh "Working on" + "recent wins".
4. Update `CLAUDE/BACKLOG.md` — add v1.1 items (rotor shrapnel anim, custom HP→stack curve, vehicle-burn sprites, medic extinguish, stop-and-roll, water extinguish, EVA "crew rescued").
5. Promote spec + plan files to `CLAUDE/sessions/<YYMMDD>_crew_evac.md` if appropriate.
6. Auto-commit all changes.
7. Review CLAUDE.md for new patterns — anything to document?

---

## Self-Review (verify before handing off to executor)

**Spec coverage:**

- D1 onfire band → Task 3 (`HpToOnFireStacks`) + Task 4 (trait)
- D2 OnFireFromHealth trait → Task 4
- D3 critical DOT unchanged → no task needed; existing system kept
- D4 EvacResolver shared → Tasks 1, 2, 3
- D5 crew + passenger unified critical-state staged → Tasks 7, 9
- D6 ±100% damage variance → Task 1 (`RollDamage` jitter 0..200) + Task 7/9 caller
- D7 staged-vs-death inherit divergence → Task 2 + Task 7/9 callers
- D8 CrewFireTransferPct YAML knob → Task 7 (Info field) + Task 8/10 (YAML)
- D9 pacing values → Tasks 7 (crew), 9 (passenger)
- D10 5-tier visual overlays → Tasks 5, 6
- D11 heli state machine flip → Task 11
- D12 mid-glide death rules → Task 12
- D13 re-entry deletion → Task 16
- D14 critical = unrecoverable + no "repaired out" branch → Task 7 step 4 + Task 14
- D15 EjectionSurvivalRate removed → Task 7 step 5
- D16 selection priority/class → Task 15
- D17 SMG/ammo → Task 15
- D18 cashback values → Task 15
- D19 default stances → Task 15 step 4

**Placeholder scan:**

- Task 12 step 3 contains a "may need to also queue an explicit Falls/HeliCrashLand" hedge that requires the engineer to read existing activities and adapt. This is necessary because the existing activity wiring isn't in the spec; the engineer must verify and adapt. Marked as a verify-during-implementation note, not a blanket "TBD."
- Task 14 step 1 says "if not present, add" — concrete fallback YAML provided.
- Task 15 step 4 says "If this is set per-actor in YAML instead, find the existing pattern" — concrete search command provided.

These are all bounded discovery steps, not unbounded TBDs.

**Type consistency:**

- `EvacResolver.RollDamage(int finishingDamage, int vehicleMaxHP, int occupantMaxHP, int jitterPercent)` — used consistently in Tasks 1, 7, 9.
- `EvacResolver.InheritOnFireStacks(int vehicleStacks, int transferPct, bool isDeathPath)` — used consistently in Tasks 2, 7, 9.
- `EvacResolver.HpToOnFireStacks(int hpPct, int startHealthPct, int bandSize, int maxStacks)` — used in Tasks 3, 4.
- `OnFireFromHealth.CurrentStacks` (int property) — used in Tasks 4, 7, 9.
- `Cargo.SuppressEjection` (bool property) — added in Task 9, used in Task 11.
- `VehicleCrew.SuppressEjection` (bool property) — already exists, used in Task 11.
- `VehicleCrewInfo.CrewFireTransferPct` (int) and `StoppedTicksRequired` (int) — added in Task 7, referenced in Task 8.
- `CargoInfo.EjectOnCritical/EjectionDelay/EjectionDelayVariance/PostStopDelay/StopTimeout/StoppedTicksRequired/CrewFireTransferPct` — added in Task 9, referenced in Task 10.
- `HeliEmergencyLandingInfo.RotorDestroyDamageThresholdPct/RotorDestroyedCondition` — added in Task 12, referenced in Task 13.

All consistent. Ready for execution.
