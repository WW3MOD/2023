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
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits.BotModules.Squads
{
	public enum SquadType { Assault, Air, Rush, Protection, Naval, Helicopter }

	public class Squad
	{
		public List<Actor> Units = new List<Actor>();
		public SquadType Type;

		internal IBot Bot;
		internal World World;
		internal SquadManagerBotModule SquadManager;
		internal MersenneTwister Random;

		internal Target Target;
		internal StateMachine FuzzyStateMachine;

		/// <summary>Optional waypoint hint for multi-axis attacks. Squad will attack-move
		/// toward this cell before engaging the main target.</summary>
		internal CPos? ApproachWaypoint;

		/// <summary>Phase 4 strategic-target pinning (experimental, heli squads). The DURABLE
		/// strategic objective the squad is committed to, kept SEPARATE from the tactical
		/// <see cref="TargetActor"/>: FSM micro-transitions (too-hot soft-swaps, tactical-target
		/// death, withdraw/re-engage) re-aim <see cref="TargetActor"/> but never touch this pin,
		/// so 5-tick predicate flapping can no longer churn the strategic destination (root cause C,
		/// design §1.3/§3.3). null = unpinned (legacy: TargetActor is both strategic and tactical).
		/// The pin is the heli-squad analogue of a Mission.Objective; released only by the abort
		/// triggers in <see cref="HelicopterStateBase"/> (objective invalid / too-hot-no-divert /
		/// stalled / bounded window). Transient bot state — not serialized (mirrors ApproachWaypoint);
		/// only ever written on the experimental pinning path ⇒ byte-identical when the flag is off.</summary>
		internal Actor StrategicTarget;

		/// <summary>World tick the current <see cref="StrategicTarget"/> pin was committed — feeds the
		/// bounded commit-window backstop (design §3.3 TTL valve). Only meaningful while StrategicTarget != null.</summary>
		internal int StrategicCommitTick;

		public Squad(IBot bot, SquadManagerBotModule squadManager, SquadType type)
			: this(bot, squadManager, type, null) { }

		public Squad(IBot bot, SquadManagerBotModule squadManager, SquadType type, Actor target)
		{
			Bot = bot;
			SquadManager = squadManager;
			World = bot.Player.PlayerActor.World;
			Random = World.LocalRandom;
			Type = type;
			Target = Target.FromActor(target);
			FuzzyStateMachine = new StateMachine();

			switch (type)
			{
				case SquadType.Assault:
				case SquadType.Rush:
					FuzzyStateMachine.ChangeState(this, new GroundUnitsIdleState());
					break;
				case SquadType.Air:
					FuzzyStateMachine.ChangeState(this, new AirIdleState());
					break;
				case SquadType.Protection:
					FuzzyStateMachine.ChangeState(this, new UnitsForProtectionIdleState());
					break;
				case SquadType.Naval:
					FuzzyStateMachine.ChangeState(this, new NavyUnitsIdleState());
					break;
				case SquadType.Helicopter:
					FuzzyStateMachine.ChangeState(this, new HelicopterIdleState());
					break;
			}
		}

		public void Update()
		{
			if (IsValid)
				FuzzyStateMachine.Update(this);
		}

		public bool IsValid => Units.Count > 0;

		public Actor TargetActor
		{
			get => Target.Actor;
			set => Target = Target.FromActor(value);
		}

		public bool IsTargetValid => Target.IsValidFor(Units.FirstOrDefault()) && !Target.Actor.Info.HasTraitInfo<HuskInfo>();

		public bool IsTargetVisible => TargetActor.CanBeViewedByPlayer(Bot.Player);

		public WPos CenterPosition { get { return Units.Select(u => u.CenterPosition).Average(); } }

		public MiniYaml Serialize()
		{
			var nodes = new MiniYaml("", new List<MiniYamlNode>()
			{
				new MiniYamlNode("Type", FieldSaver.FormatValue(Type)),
				new MiniYamlNode("Units", FieldSaver.FormatValue(Units.Select(a => a.ActorID).ToArray())),
			});

			if (Target.Type == TargetType.Actor)
				nodes.Nodes.Add(new MiniYamlNode("Target", FieldSaver.FormatValue(Target.Actor.ActorID)));

			return nodes;
		}

		public static Squad Deserialize(IBot bot, SquadManagerBotModule squadManager, MiniYaml yaml)
		{
			var type = SquadType.Rush;
			Actor targetActor = null;

			var typeNode = yaml.Nodes.FirstOrDefault(n => n.Key == "Type");
			if (typeNode != null)
				type = FieldLoader.GetValue<SquadType>("Type", typeNode.Value.Value);

			var targetNode = yaml.Nodes.FirstOrDefault(n => n.Key == "Target");
			if (targetNode != null)
				targetActor = squadManager.World.GetActorById(FieldLoader.GetValue<uint>("ActiveUnits", targetNode.Value.Value));

			var squad = new Squad(bot, squadManager, type, targetActor);

			var unitsNode = yaml.Nodes.FirstOrDefault(n => n.Key == "Units");
			if (unitsNode != null)
				squad.Units.AddRange(FieldLoader.GetValue<uint[]>("Units", unitsNode.Value.Value)
					.Select(a => squadManager.World.GetActorById(a)));

			return squad;
		}
	}
}
