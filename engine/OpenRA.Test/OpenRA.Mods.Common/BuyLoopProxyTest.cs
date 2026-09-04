#region Copyright & License Information
/*
 * WW3MOD buy-loop proxy — pins the trait set of a BODILESS support-power proxy against the exact
 * check the merge gate runs.
 *
 * WHY THIS EXISTS. `tools/autotest/scenarios/test-power-buy-loop` defines `powerproxy.strike`, an
 * actor with no IOccupySpace, to answer proposal §8 item 4 ("does a bodiless proxy really
 * produce?"). It shipped at 4c0a5e2a carrying a `Tooltip:` and failed `make.ps1 test` with
 *
 *     Actor `powerproxy.strike` is not constructible; failure:
 *     ActorInfo("powerproxy.strike") failed to initialize because of the following:
 *     Missing:
 *     OpenRA.Traits.IMouseBoundsInfo
 *     Unresolved:
 *     OpenRA.Mods.Common.Traits.TooltipInfo: { OpenRA.Traits.IMouseBoundsInfo }
 *
 * because `TooltipInfoBase : ConditionalTraitInfo, Requires<IMouseBoundsInfo>` (Tooltip.cs:16) and
 * a positionless actor has no Selectable, no IsometricSelectable and no Interactable to supply it.
 *
 * THE POINT OF PINNING IT HERE rather than trusting the gate. A bodiless proxy is a shape nothing
 * else in the mod uses — the three `powerproxy.*` blocks in misc.yaml are commented out and carry
 * neither RenderSprites nor Tooltip — so the only thing standing between the next author and the
 * same failure is a comment. This calls ActorInfo.TraitsInConstructOrder() directly, which is the
 * very method whose exception CheckTraitPrerequisites.cs:42 reports, so a green run here means the
 * gate agrees. It costs no mod load, no World and no launch slot.
 *
 * MAINTENANCE: the trait list below MIRRORS the scenario's rules.yaml by hand — a flat text reader
 * cannot turn MiniYaml into TraitInfo instances without an ObjectCreator and a loaded mod. The
 * ScenarioStillDeclaresTheTraitsThisFixtureModels test is what stops the two drifting apart.
 */
#endregion

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;

namespace OpenRA.Test
{
	[TestFixture]
	public class BuyLoopProxyTest
	{
		const string Scenario = "test-power-buy-loop";
		const string ProxyActor = "powerproxy.strike";

		/// <summary>The proxy exactly as the scenario declares it. Order is irrelevant —
		/// TraitsInConstructOrder resolves the dependency graph itself.</summary>
		static ActorInfo Proxy()
		{
			return new ActorInfo(ProxyActor,
				new ValuedInfo(),
				new TooltipInfo(),
				new RenderSpritesInfo(),
				new AlwaysVisibleInfo(),
				new InteractableInfo(),
				new BuildableInfo(),
				new MissileStrikePowerInfo());
		}

		[Test]
		public void TheBodilessProxyIsConstructible()
		{
			Assert.DoesNotThrow(() => Proxy().TraitsInConstructOrder(),
				"the buy-loop proxy no longer resolves its traits, which is exactly how it failed " +
				"the merge gate at 4c0a5e2a. Read the exception: it names the unsatisfied interface.");
		}

		[Test]
		public void RemovingInteractableIsWhatBreaksIt()
		{
			// THE NEGATIVE HALF, and the reason it is here: without it, someone tidying an
			// "unused" Interactable off a positionless actor gets a green fixture and a red gate.
			// This states, in the place they would look, that the trait is load-bearing and why.
			var withoutBounds = new ActorInfo(ProxyActor,
				new ValuedInfo(),
				new TooltipInfo(),
				new RenderSpritesInfo(),
				new AlwaysVisibleInfo(),
				new BuildableInfo(),
				new MissileStrikePowerInfo());

			var ex = Assert.Throws<YamlException>(() => withoutBounds.TraitsInConstructOrder());

			Assert.That(ex.Message, Does.Contain("IMouseBoundsInfo"),
				"the failure must still be the mouse-bounds one this fixture documents; if it has " +
				"become a different unsatisfied dependency, re-derive the trait set");
			Assert.That(ex.Message, Does.Contain("TooltipInfo"),
				"Tooltip is the trait that carries the requirement. Note that removing IT instead is " +
				"NOT the fix — see DroppingTooltipIsNotAValidAlternativeFix");
		}

		[Test]
		public void DroppingTooltipIsNotAValidAlternativeFix()
		{
			// THE OBVIOUS-LOOKING SHORTCUT, AND WHY IT IS WRONG. Deleting Tooltip does make the
			// actor constructible — the first assertion below proves that much, and it is what makes
			// the shortcut tempting. But it only moves the failure to a different lint pass:
			// CheckTooltips emits "The following buildable actor has no (enabled) Tooltip" for ANY
			// actor carrying BuildableInfo (CheckTooltips.cs:28-35), and a buy-menu item must carry
			// Buildable. The three traits are therefore locked in a chain that a bodiless actor can
			// only close at one end:
			//
			//     Buildable =(CheckTooltips)=> Tooltip =(Requires)=> IMouseBoundsInfo <= Interactable
			//
			// This was very nearly shipped as a documented alternative in the scenario's rules.yaml.
			// It would have traded one red gate for another.
			var withoutTooltip = new ActorInfo(ProxyActor,
				new ValuedInfo(),
				new RenderSpritesInfo(),
				new AlwaysVisibleInfo(),
				new BuildableInfo(),
				new MissileStrikePowerInfo());

			Assert.DoesNotThrow(() => withoutTooltip.TraitsInConstructOrder(),
				"precondition: dropping Tooltip really does satisfy TraitsInConstructOrder — that is " +
				"exactly why the shortcut looks safe. If this throws, some OTHER trait on the proxy " +
				"has acquired an unsatisfied dependency and the reasoning below needs redoing.");

			// The chain, read off the scenario file rather than assumed, so it goes red if a future
			// edit breaks any link — including someone dropping Buildable, which WOULD make removing
			// Tooltip legitimate and this test's premise obsolete.
			var declared = TopLevelTraitsOf(ScenarioRules(), ProxyActor);

			Assert.That(declared, Does.Contain("Buildable"),
				"the proxy is a buy-menu item; if it has stopped being Buildable, CheckTooltips no " +
				"longer applies to it and dropping Tooltip becomes a real option");
			Assert.That(declared, Does.Contain("Tooltip"),
				"CheckTooltips requires an enabled Tooltip on every buildable actor");
			Assert.That(declared, Does.Contain("Interactable"),
				"and Tooltip requires IMouseBoundsInfo, which on a positionless actor only " +
				"Interactable can supply");
		}

		[Test]
		public void ScenarioStillDeclaresTheTraitsThisFixtureModels()
		{
			// The drift guard. Everything above reasons about a hand-written trait list; this is
			// what ties that list to the file the gate actually reads.
			var path = ScenarioRules();
			var declared = TopLevelTraitsOf(path, ProxyActor);

			Assert.That(declared, Is.Not.Empty, $"{ProxyActor} not found in {path}");

			foreach (var required in new[] { "Valued", "Tooltip", "RenderSprites", "AlwaysVisible", "Interactable", "Buildable" })
				Assert.That(declared, Does.Contain(required),
					$"{ProxyActor} no longer declares `{required}`, so the trait set modelled in this " +
					"fixture is out of step with the scenario and its green says nothing about the gate");

			Assert.That(declared.Any(t => t.StartsWith("MissileStrikePower", StringComparison.Ordinal)), Is.True,
				$"{ProxyActor} no longer carries a MissileStrikePower, which is the whole point of the proxy");

			// Anything NEW on the proxy is unmodelled, and an unmodelled trait is exactly how the
			// original failure got in — Tooltip was added without anyone asking what it required.
			var modelled = new[] { "Valued", "Tooltip", "RenderSprites", "AlwaysVisible", "Interactable", "Buildable" };
			var unmodelled = declared.Where(t =>
				!modelled.Contains(t) && !t.StartsWith("MissileStrikePower", StringComparison.Ordinal)).ToArray();

			Assert.That(unmodelled, Is.Empty,
				$"{ProxyActor} declares trait(s) this fixture does not model: {string.Join(", ", unmodelled)}. " +
				"Add them to Proxy() above and re-run, or the constructibility assertions are checking " +
				"a shape the gate does not see.");
		}

		static string ScenarioRules()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				var candidate = Path.Combine(dir.FullName, "tools", "autotest", "scenarios", Scenario, "rules.yaml");
				if (File.Exists(candidate))
					return candidate;

				dir = dir.Parent;
			}

			throw new FileNotFoundException($"could not locate tools/autotest/scenarios/{Scenario}/rules.yaml");
		}

		/// <summary>The indent-1 trait keys under one top-level actor, with any `@suffix` kept —
		/// flat text, the same approach the other WW3MOD YAML pins take.</summary>
		static string[] TopLevelTraitsOf(string path, string actor)
		{
			var traits = new System.Collections.Generic.List<string>();
			var inActor = false;

			foreach (var raw in File.ReadLines(path))
			{
				var line = raw.Split('#')[0].TrimEnd();
				if (line.Trim().Length == 0)
					continue;

				var indent = line.TakeWhile(c => c == '\t').Count();
				var body = line.Trim();

				if (indent == 0)
				{
					if (inActor)
						break;

					inActor = body == actor + ":";
				}
				else if (indent == 1 && inActor)
					traits.Add(body.Split(':')[0].Trim());
			}

			return traits.ToArray();
		}
	}
}
