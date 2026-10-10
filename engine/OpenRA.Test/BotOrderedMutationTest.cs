#region Copyright & License Information
/*
 * WW3MOD bot-layer ordered-mutation discipline (2026-09-22).
 *
 * THE INCIDENT THIS PINS. Saved-game restore was RED twice. The second leak was located to one net
 * frame and one unit: net frame 711 / world tick 2130, actor 4712 at.america owned by Russia-bot,
 * Mobile.Facing 256 in the recording against 118 in the replay, with AttackFrontal.IsAiming true only
 * on the replay side (dc8e0abf). The cause, once the visibility hypothesis had been refuted by
 * measurement, was one line: LaneAmbushBotModule granted enable-ambush-tactics by calling
 * ExternalCondition.GrantCondition directly from a bot tick. A direct grant is not an order, so
 * GameSave never recorded it; ModularBot.BotTick early-returns while World.IsLoadingGameSave
 * (ModularBot.cs:241, :339), so the restored life never granted it; and the gate is read by SYNCED
 * code — AttackMoveActivity's halt-before-contact (AttackMoveActivity.cs:189-206). The recording
 * halted the march and held its facing; the replay took the engage branch, queued an attack activity
 * and turned the unit to aim. That IS the 256-vs-118 pair. Fixed at 61546a51 by routing all three
 * condition mutations through Order("SetAmbushGate"), resolved in AutoTarget.ResolveOrder (:619-620).
 *
 * WHY THIS IS A UNIT TEST AND NOT A SCENARIO. The dynamic instrument already exists and is green:
 * tools/autotest/scenarios/test-savegame-resume-riverzeta drives save-at-tick-3000 and reload in one
 * process via GameSaveRoundTripProbe, and the engine's own sync hash validates the restore
 * (GameSave.cs:262-263, OrderManager.ReceiveSync :202-211) — strictly stronger than asserting one
 * field on one actor. What it costs is a 4-6 minute launch, and what it cannot tell you is WHICH
 * mutation escaped the order stream: it goes red identically for every member of the class. The
 * class is bounded by READING, not by running — that was the explicit conclusion of the investigation
 * (DISCOVERIES 2026-08-16: "a STATIC audit, not a dynamic sweep"), because the whole-match SyncHash
 * sweep that preceded it was structurally incapable of seeing a condition grant: granting changes no
 * [Sync]-marked field at the moment it happens.
 *
 * WHAT A GREEN HERE DOES NOT SAY. It does not say restore is deterministic. It says no bot-layer
 * method reachable by this scan performs one of the six named direct mutations. The reverse class
 * the investigation left open — synced code reading state only bot ticks refresh — is untouched by
 * this fixture and has no detector.
 *
 * SCAN CAVEAT, inherited from IlScan and restated because the load-bearing assertion here is a
 * NEGATIVE one. The walk is linear over the IL bytes, so a byte mid-operand that looks like a call
 * opcode is resolved too. That can only ADD a callee, so a spurious hit would be a false FAILURE,
 * never a false pass — the safe direction for this fixture, and the direction to suspect first if it
 * ever goes red with no edit behind it. Both tests assert a floor on what the scan resolved so a
 * scanner that silently resolves nothing cannot read as a clean bot layer.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BotOrderedMutationTest
	{
		// The incident, quoted into every failure so a future red lands on the record rather than on a
		// rule whose reason has to be reconstructed.
		const string Incident =
			"SAVED-GAME RESTORE DESYNC (dc8e0abf / fixed 61546a51). Bot logic runs host-only " +
			"(Player.cs:224-232) and is suppressed entirely while a save is being restored " +
			"(ModularBot.cs:241, :339). A mutation made directly from a bot tick therefore happens on " +
			"exactly one client and never happens again on restore, while every other client and the " +
			"restored life carry on without it. Last time this shipped, actor 4712 at.america held " +
			"Mobile.Facing 256 in the recording and 118 in the replay — the recording halted its " +
			"attack-move for an ambush and the replay turned to aim instead — and the restore failed " +
			"its validating sync-hash comparison at net frame 711 / world tick 2130. " +
			"Issue an Order instead: bot.QueueOrder(new Order(...)), resolved by a trait on the " +
			"target actor. LaneAmbushBotModule.EnsureGatedAmbusher is the worked example.";

		/// <summary>
		/// The bot layer: every trait that ModularBot ticks, plus the stateless helper classes those
		/// modules delegate to. The helpers are picked up by name because they are ordinary static
		/// classes in the same namespace as every other trait — the directory that groups them is not
		/// visible to reflection. Their names follow the layer's own convention, which is what this
		/// asserts a floor on below.
		/// </summary>
		// The wire name the two halves have to agree on. Named once here so a test that checks both
		// sides cannot be "fixed" by editing one of them.
		const string GateOrder = "SetAmbushGate";

		// Same rule for the out-of-ammo rearm dispatch fixed 2026-10-10.
		const string RearmOrder = "RearmAtHost";

		static readonly string[] HelperSuffixes = { "Math", "Tactics", "Gate", "Guard", "Blackboard" };

		static IEnumerable<Type> BotLayerTypes()
		{
			var assembly = typeof(AutoTarget).Assembly;

			var modules = assembly.GetTypes()
				.Where(t => typeof(IBotTick).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
				.ToList();

			// The helpers a bot tick delegates to. Restricted to types the modules actually mention, so
			// this cannot quietly widen into unrelated code that merely shares a name suffix.
			var mentioned = new HashSet<Type>();
			foreach (var m in modules)
				foreach (var method in DeclaredMethods(m))
					foreach (var callee in IlScan.Scan(method).Callees)
						if (callee.DeclaringType != null && callee.DeclaringType.Assembly == assembly)
							mentioned.Add(callee.DeclaringType);

			var helpers = mentioned
				.Where(t => !typeof(IBotTick).IsAssignableFrom(t)
					&& HelperSuffixes.Any(s => t.Name.EndsWith(s, StringComparison.Ordinal)));

			return modules.Concat(helpers).Distinct();
		}

		static IEnumerable<MethodBase> DeclaredMethods(Type t)
		{
			const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
				| BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

			foreach (var m in t.GetMethods(Flags))
				yield return m;

			foreach (var c in t.GetConstructors(Flags))
				yield return c;

			// Closures and iterator state machines compile into nested types; a grant written inside a
			// lambda in a bot tick lives there, not on the module.
			foreach (var nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
				foreach (var m in DeclaredMethods(nested))
					yield return m;
		}

		/// <summary>
		/// The six direct writes to synced world state that a bot tick must never make itself. Each is
		/// matched on declaring type plus name, so an unrelated method that happens to share a name is
		/// not swept up.
		/// </summary>
		static bool IsUnorderedMutation(MethodBase callee)
		{
			var type = callee.DeclaringType;
			if (type == null)
				return false;

			if (type == typeof(Actor))
				return callee.Name == "GrantCondition" || callee.Name == "RevokeCondition"
					|| callee.Name == "QueueActivity" || callee.Name == "CancelActivity";

			if (type == typeof(ExternalCondition))
				return callee.Name == "GrantCondition" || callee.Name == "TryRevokeCondition";

			return false;
		}

		// THE PIN. Not "LaneAmbushBotModule does not grant a condition" — the whole class, because the
		// investigation found the same defect twice in two different forms (an activity write, then a
		// condition grant) and the sweep that was supposed to bound it could see neither.
		[Test]
		public void NoBotModuleMutatesSyncedStateOutsideTheOrderStream()
		{
			var types = BotLayerTypes().ToList();

			Assert.That(types.Count, Is.GreaterThan(20),
				$"Only {types.Count} bot-layer types found — IBotTick has been renamed or the module " +
				"traits have moved, and this fixture is scanning almost nothing rather than finding a " +
				"clean bot layer.");

			Assert.That(types.Any(t => t.Name == "LaneAmbushBotModule"), Is.True,
				"LaneAmbushBotModule is not in the scanned set. It is the module the original defect " +
				"shipped in, so its absence means the type selection is wrong.");

			var resolved = 0;
			var offences = new List<string>();

			// ONE HOP DEEPER (2026-10-10). The mutation that shipped in PoiOffensiveBotModule's
			// out-of-ammo sweep was not in the module: it called AmmoPool.AutoRearm, and AutoRearm
			// called QueueActivity(false, ...). AmmoPool matches no helper suffix, so the scan above
			// never opened it. Every same-assembly callee outside the bot layer is now opened once and
			// checked for the same six mutations — one level only, not a transitive closure: deeper
			// walks reach the order resolvers themselves (ResolveOrder -> ... -> QueueActivity) through
			// shared helpers, and a fixture that cannot tell "the bot ordered it" from "the bot did it"
			// is one that gets switched off.
			var botLayer = new HashSet<Type>(types);
			var assembly = typeof(AutoTarget).Assembly;
			var hopCache = new Dictionary<MethodBase, List<MethodBase>>();
			var hopsOpened = 0;

			foreach (var type in types)
			{
				foreach (var method in DeclaredMethods(type))
				{
					var scan = IlScan.Scan(method);
					resolved += scan.ResolvedCalls;

					foreach (var callee in scan.Callees.Where(IsUnorderedMutation).Distinct())
						offences.Add($"{type.FullName}.{method.Name} calls " +
							$"{callee.DeclaringType.Name}.{callee.Name}");

					foreach (var callee in scan.Callees.Distinct())
					{
						var owner = callee.DeclaringType;
						if (owner == null || owner.Assembly != assembly || botLayer.Contains(owner)
							|| (owner.DeclaringType != null && botLayer.Contains(owner.DeclaringType)))
							continue;

						if (!hopCache.TryGetValue(callee, out var mutations))
						{
							mutations = IlScan.Scan(callee).Callees.Where(IsUnorderedMutation).Distinct().ToList();
							hopCache[callee] = mutations;
							hopsOpened++;
						}

						foreach (var mutation in mutations)
							offences.Add($"{type.FullName}.{method.Name} calls " +
								$"{owner.Name}.{callee.Name}, which calls " +
								$"{mutation.DeclaringType.Name}.{mutation.Name}");
					}
				}
			}

			Assert.That(hopsOpened, Is.GreaterThan(100),
				$"Only {hopsOpened} same-assembly callees were opened for the second hop — the hop " +
				"filter is excluding nearly everything, not finding a clean bot layer.");

			Assert.That(resolved, Is.GreaterThan(2000),
				$"IL scan resolved only {resolved} call tokens across {types.Count} bot-layer types — " +
				"the scanner is broken, not the bot layer clean.");

			Assert.That(offences, Is.Empty,
				"A bot module writes synced world state directly instead of issuing an order:" +
				Environment.NewLine + string.Join(Environment.NewLine, offences.Select(o => "  " + o)) +
				Environment.NewLine + Environment.NewLine + Incident);
		}

		// THE WORKED EXAMPLE, pinned positively so the rule above cannot be satisfied by deleting the
		// mechanism. A negative assertion passes vacuously once the code it forbids is gone for any
		// reason at all, including the gate being dropped.
		[Test]
		public void TheAmbushGateStillTravelsAsAnOrder()
		{
			var module = typeof(AutoTarget).Assembly
				.GetType("OpenRA.Mods.Common.Traits.LaneAmbushBotModule");

			Assert.That(module, Is.Not.Null,
				"OpenRA.Mods.Common.Traits.LaneAmbushBotModule not found — this fixture is no longer " +
				"pinning the site the saved-game restore fix landed in.");

			var ensure = module.GetMethod("EnsureGatedAmbusher",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

			Assert.That(ensure, Is.Not.Null,
				"LaneAmbushBotModule.EnsureGatedAmbusher not found. It is the method that granted the " +
				"enable-ambush-tactics condition directly and now orders it; a rename must fail here " +
				"loudly rather than skip the scan.");

			var scan = IlScan.Scan(ensure);

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(5),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in EnsureGatedAmbusher — the " +
				"scanner is broken, not the method clean.");

			var buildsAnOrder = scan.Callees
				.OfType<ConstructorInfo>()
				.Any(c => c.DeclaringType == typeof(Order));

			Assert.That(buildsAnOrder, Is.True,
				"LaneAmbushBotModule.EnsureGatedAmbusher no longer constructs an Order. The gate it " +
				"applies is read by synced code (AttackMoveActivity's halt-before-contact), so it has " +
				"to reach every client and the saved order stream as an order." +
				Environment.NewLine + Environment.NewLine + Incident);

			var queuesIt = scan.Callees.Any(c => c.Name == "QueueOrder");

			Assert.That(queuesIt, Is.True,
				"LaneAmbushBotModule.EnsureGatedAmbusher builds an Order but never hands it to " +
				"IBot.QueueOrder, so nothing issues it." +
				Environment.NewLine + Environment.NewLine + Incident);

			var literals = IlScan.ScanStringLiterals(ensure);

			Assert.That(literals, Is.Not.Empty,
				"IL scan found no string literals in EnsureGatedAmbusher — the scanner is broken, not " +
				"the order string missing.");

			Assert.That(literals, Contains.Item(GateOrder),
				$"LaneAmbushBotModule.EnsureGatedAmbusher issues no Order named \"{GateOrder}\" " +
				$"(literals found: {string.Join(", ", literals)}). AutoTarget.ResolveOrder matches on " +
				"that exact string, and a mismatch is silent: the order is issued, nothing resolves " +
				"it, the gate is never applied, and the ambush halt stops happening with no error " +
				"anywhere." + Environment.NewLine + Environment.NewLine + Incident);
		}

		// THE RECEIVING HALF. An ordered grant is only a fix if something resolves the order; without
		// this, deleting the AutoTarget arm leaves both tests above green and the gate permanently off.
		[Test]
		public void AutoTargetStillResolvesTheAmbushGateOrder()
		{
			// AutoTarget implements IResolveOrder EXPLICITLY (AutoTarget.cs:600), so the method is named
			// OpenRA.Traits.IResolveOrder.ResolveOrder and GetMethod("ResolveOrder") returns null. Going
			// through the interface map finds it whichever way it is declared.
			var map = typeof(AutoTarget).GetInterfaceMap(typeof(IResolveOrder));
			var resolveOrder = map.TargetMethods
				.FirstOrDefault(m => map.InterfaceMethods[Array.IndexOf(map.TargetMethods, m)].Name == "ResolveOrder");

			Assert.That(resolveOrder, Is.Not.Null,
				"AutoTarget no longer implements IResolveOrder.ResolveOrder — it is the trait that " +
				"receives the ordered ambush-gate grant.");

			var setter = typeof(AutoTarget).GetMethod("SetAmbushGate",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

			Assert.That(setter, Is.Not.Null,
				"AutoTarget.SetAmbushGate not found. It is the ordered receiver the grant was moved " +
				"into at 61546a51; without it the SetAmbushGate order is issued and dropped, the gate " +
				"is never applied, and the ambush halt silently stops happening at all." +
				Environment.NewLine + Environment.NewLine + Incident);

			var scan = IlScan.Scan(resolveOrder);

			Assert.That(scan.ResolvedCalls, Is.GreaterThan(3),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in AutoTarget.ResolveOrder — the " +
				"scanner is broken, not the arm missing.");

			Assert.That(scan.Callees.Any(c => c.Name == "SetAmbushGate"), Is.True,
				"AutoTarget.ResolveOrder no longer dispatches to SetAmbushGate, so the SetAmbushGate " +
				"order LaneAmbushBotModule issues resolves to nothing." +
				Environment.NewLine + Environment.NewLine + Incident);

			// The half a call scan cannot see. Both sides still exist and still call each other's
			// methods when the wire name drifts; only the literal says whether they ever meet.
			var literals = IlScan.ScanStringLiterals(resolveOrder);

			Assert.That(literals, Is.Not.Empty,
				"IL scan found no string literals in AutoTarget.ResolveOrder — the scanner is broken, " +
				"not the arm missing.");

			Assert.That(literals, Contains.Item(GateOrder),
				$"AutoTarget.ResolveOrder matches no order named \"{GateOrder}\" (literals found: " +
				$"{string.Join(", ", literals)}). LaneAmbushBotModule issues exactly that string, so " +
				"the two halves no longer meet: the grant is ordered, dropped on arrival, and the " +
				"ambush halt is inert with nothing logged." +
				Environment.NewLine + Environment.NewLine + Incident);
		}


		// BOTH HALVES OF THE REARM ORDER (2026-10-10). PoiOffensiveBotModule.SweepOutOfAmmoUnits called
		// AmmoPool.AutoRearm directly — a QueueActivity that cancelled the unit's current activity on the
		// host only. The negative scan above now sees that one hop down; this pins that the fix still
		// travels as an order, that AmmoPool still resolves it, and that the wire names still meet.
		[Test]
		public void TheOutOfAmmoRearmStillTravelsAsAnOrderAmmoPoolResolves()
		{
			var module = typeof(AutoTarget).Assembly
				.GetType("OpenRA.Mods.Common.Traits.PoiOffensiveBotModule");
			var sweep = module?.GetMethod("SweepOutOfAmmoUnits",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

			Assert.That(sweep, Is.Not.Null,
				"PoiOffensiveBotModule.SweepOutOfAmmoUnits not found — this fixture is no longer pinning " +
				"the site the out-of-ammo rearm desync was fixed in.");

			var scan = IlScan.Scan(sweep);
			Assert.That(scan.ResolvedCalls, Is.GreaterThan(5),
				$"IL scan resolved only {scan.ResolvedCalls} tokens in SweepOutOfAmmoUnits — the scanner " +
				"is broken, not the method clean.");

			Assert.That(scan.Callees.Any(c => c.Name == "QueueOrder"), Is.True,
				"SweepOutOfAmmoUnits no longer hands an Order to IBot.QueueOrder, so the rearm dispatch " +
				"is either gone or back to a direct host-only call." +
				Environment.NewLine + Environment.NewLine + Incident);

			Assert.That(IlScan.ScanStringLiterals(sweep), Contains.Item(RearmOrder),
				$"SweepOutOfAmmoUnits issues no Order named \"{RearmOrder}\". AmmoPool.ResolveOrder " +
				"matches that exact string; a mismatch is silent and dry bot units stop rearming." +
				Environment.NewLine + Environment.NewLine + Incident);

			var map = typeof(AmmoPool).GetInterfaceMap(typeof(IResolveOrder));
			var resolveOrder = map.TargetMethods
				.FirstOrDefault(m => map.InterfaceMethods[Array.IndexOf(map.TargetMethods, m)].Name == "ResolveOrder");

			Assert.That(resolveOrder, Is.Not.Null,
				"AmmoPool no longer implements IResolveOrder.ResolveOrder — it is the trait that " +
				"receives the ordered rearm dispatch.");

			var resolveScan = IlScan.Scan(resolveOrder);
			Assert.That(resolveScan.Callees.Any(c => c.DeclaringType == typeof(AmmoPool) && c.Name == "AutoRearm"),
				Is.True,
				"AmmoPool.ResolveOrder no longer calls AutoRearm, so the RearmAtHost order resolves to " +
				"nothing and dry bot units park where they emptied." +
				Environment.NewLine + Environment.NewLine + Incident);

			Assert.That(IlScan.ScanStringLiterals(resolveOrder), Contains.Item(RearmOrder),
				$"AmmoPool.ResolveOrder matches no order named \"{RearmOrder}\"; PoiOffensiveBotModule " +
				"issues exactly that string, so the two halves no longer meet." +
				Environment.NewLine + Environment.NewLine + Incident);
		}
	}
}
