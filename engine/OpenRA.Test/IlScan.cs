#region Copyright & License Information
/*
 * WW3MOD IL scanning shared by the structural fixtures (2026-09-01).
 *
 * Two fixtures now pin a property that no autotest can reach — FrozenActorTargetingTest (a cursor
 * must not read state fog hides) and GroupScatterWaypointTest (a replayed waypoint must not be a
 * per-unit value). Both answer their question by asking what a method CALLS, so the scanner lives
 * here rather than in either of them. It was copied once already; a second copy would be the point
 * at which the two silently drift apart and one of them quietly stops scanning anything.
 *
 * DELIBERATELY NAIVE. This walks the bytes linearly rather than decoding the instruction stream, so
 * a byte that merely LOOKS like a call opcode mid-operand is resolved too. That is safe in both
 * directions here: a bogus token throws and is swallowed, and a token that happens to resolve is a
 * method this body genuinely mentions. What it must never do is silently resolve NOTHING and read
 * as clean, which is why every caller asserts a floor on ResolvedCalls.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Reflection;

namespace OpenRA.Test
{
	static class IlScan
	{
		const byte CallOpcode = 0x28;
		const byte CallvirtOpcode = 0x6F;
		const byte NewobjOpcode = 0x73;

		public sealed class Result
		{
			public readonly List<MethodBase> Callees = new List<MethodBase>();
			public int ResolvedCalls;
		}

		/// <summary>
		/// Every method or constructor token reachable through a call, callvirt or newobj in this
		/// method body. A body-less method (abstract, extern) yields an empty result rather than
		/// throwing, so callers can scan a whole assembly without filtering first.
		/// </summary>
		public static Result Scan(MethodBase method)
		{
			var result = new Result();

			var body = method.GetMethodBody();
			var il = body?.GetILAsByteArray();
			if (il == null)
				return result;

			var typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType
				? method.DeclaringType.GetGenericArguments()
				: Type.EmptyTypes;
			var methodArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : Type.EmptyTypes;

			for (var i = 0; i + 4 < il.Length; i++)
			{
				if (il[i] != CallOpcode && il[i] != CallvirtOpcode && il[i] != NewobjOpcode)
					continue;

				var token = BitConverter.ToInt32(il, i + 1);
				try
				{
					var callee = method.Module.ResolveMethod(token, typeArgs, methodArgs);
					if (callee != null)
					{
						result.ResolvedCalls++;
						result.Callees.Add(callee);
					}
				}
				catch (ArgumentException)
				{
					// Not a method token — the byte matched mid-operand of another instruction.
				}
			}

			return result;
		}
	}
}
