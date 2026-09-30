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
 *
 * ScanStringLiterals (2026-09-22) answers it about ldstr, for BotOrderedMutationTest, which has to
 * check that the order string a bot module ISSUES is the one the receiving trait RESOLVES. A
 * mismatch there is invisible to a call scan: both halves still exist and still call each other's
 * methods, the order is simply never matched and the feature is inert. Same naivety, same two
 * outcomes, same floor rule.
 *
 * ScanFieldWrites (2026-09-15) answers the same question about stfld/stsfld, for GarrisonPanelTest,
 * which has to distinguish "this logic class assigns Widget.IsVisible" from "this logic class
 * assigns Widget.Visible" -- two field stores, no call between them. The same naivety applies with
 * the same two outcomes, and the same floor rule: assert on WrittenFields.Count before reading
 * anything into an absence. NOTE the asymmetry a negative assertion inherits here -- a spurious
 * resolution can only ADD a field that is not really written, so "X is written" is the safe claim
 * and "X is not written" is the one that could in principle fail spuriously. It has not, and a
 * random mid-operand token resolving to one named field of one named type is not a realistic
 * accident, but that is the direction to suspect first if this ever goes red without an edit.
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
		const byte StfldOpcode = 0x7D;
		const byte StsfldOpcode = 0x80;
		const byte LdstrOpcode = 0x72;

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

		/// <summary>
		/// Every field token reachable through an stfld or stsfld in this method body — i.e. the
		/// fields this method ASSIGNS, ignoring the ones it merely reads. Same linear walk and the
		/// same caveats as <see cref="Scan"/>; a body-less method yields an empty list.
		/// </summary>
		public static List<FieldInfo> ScanFieldWrites(MethodBase method)
		{
			var written = new List<FieldInfo>();

			var body = method.GetMethodBody();
			var il = body?.GetILAsByteArray();
			if (il == null)
				return written;

			var typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType
				? method.DeclaringType.GetGenericArguments()
				: Type.EmptyTypes;
			var methodArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : Type.EmptyTypes;

			for (var i = 0; i + 4 < il.Length; i++)
			{
				if (il[i] != StfldOpcode && il[i] != StsfldOpcode)
					continue;

				var token = BitConverter.ToInt32(il, i + 1);
				try
				{
					var field = method.Module.ResolveField(token, typeArgs, methodArgs);
					if (field != null)
						written.Add(field);
				}
				catch (ArgumentException)
				{
					// Not a field token — the byte matched mid-operand of another instruction.
				}
			}

			return written;
		}

		/// <summary>
		/// Every string literal this method loads. Answers "do these two methods name the same order
		/// string" — an issuer and a receiver that disagree compile, link and run, and the feature is
		/// simply never reached. Same linear walk and the same caveats as <see cref="Scan"/>: a
		/// spurious mid-operand token can only ADD a literal, so "this literal is present" is the safe
		/// claim. Callers should assert a floor on the count before reading anything into an absence.
		/// </summary>
		public static List<string> ScanStringLiterals(MethodBase method)
		{
			var literals = new List<string>();

			var body = method.GetMethodBody();
			var il = body?.GetILAsByteArray();
			if (il == null)
				return literals;

			for (var i = 0; i + 4 < il.Length; i++)
			{
				if (il[i] != LdstrOpcode)
					continue;

				var token = BitConverter.ToInt32(il, i + 1);
				try
				{
					var literal = method.Module.ResolveString(token);
					if (literal != null)
						literals.Add(literal);
				}
				catch (ArgumentException)
				{
					// Not a string token — the byte matched mid-operand of another instruction.
				}
			}

			return literals;
		}
	}
}
