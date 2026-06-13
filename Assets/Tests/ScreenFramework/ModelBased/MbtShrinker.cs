using System;
using System.Collections.Generic;
using ScreenFramework;

namespace Tests.ScreenFramework.ModelBased
{
	/// <summary>
	/// 反例シナリオの縮小。失敗が再現する限り「操作の除去 → フィールドの単純化」を
	/// 繰り返し、固定点（どの 1 変更でも失敗しなくなる）まで縮める。
	/// 縮小後のシナリオがそのまま最小再現＝リグレッションテストの素になる。
	/// 縮小中は「同じプロパティの失敗」かは問わない（何かが失敗し続ければ採用）。
	/// </summary>
	public static class MbtShrinker
	{
		public static async System.Threading.Tasks.Task<MbtScenario> Shrink(MbtScenario sc, int budget = 150)
		{
			var current = sc;
			var runs = 0;
			var changed = true;
			while (changed && runs < budget)
			{
				changed = false;

				// 1. 操作の除去（消した push の uid を参照する PopTo/CloseAt は両系とも no-op になるので安全）
				for (var i = current.Ops.Count - 1; i >= 0 && runs < budget; i--)
				{
					if (current.Ops.Count <= 1) break;
					var cand = current.Clone();
					cand.Ops.RemoveAt(i);
					cand.Ops[0].Overlap = false;
					runs++;
					if (!(await MbtExecutor.Run(cand)).Ok)
					{
						current = cand;
						changed = true;
					}
				}

				// 2. フィールドの単純化
				for (var i = 0; i < current.Ops.Count && runs < budget; i++)
				{
					foreach (var simplify in Simplifications(current.Ops[i]))
					{
						var cand = current.Clone();
						simplify(cand.Ops[i]);
						runs++;
						if (!(await MbtExecutor.Run(cand)).Ok)
						{
							current = cand;
							changed = true;
							break;   // この op は作り直されたので候補列挙をやり直す
						}
						if (runs >= budget) break;
					}
				}
			}
			return current;
		}

		static IEnumerable<Action<MbtOp>> Simplifications(MbtOp op)
		{
			if (op.Fault != MbtOpFault.None) yield return o => o.Fault = MbtOpFault.None;
			if (op.Token != MbtTokenMode.None) yield return o => o.Token = MbtTokenMode.None;
			if (op.Gate != MbtGateMode.None) yield return o => o.Gate = MbtGateMode.None;
			if (op.Overlap) yield return o => o.Overlap = false;
			if (op.Priority == InterruptPriority.Preempt && op.Kind != MbtOpKind.DismissAll)
				yield return o => o.Priority = InterruptPriority.Queue;
			if (op.Screen != null && op.Screen.Faults != MbtScreenFaults.None)
				yield return o => o.Screen.Faults = MbtScreenFaults.None;
			if (op.Screen != null && op.Screen.Cache == ScreenCacheMode.KeepOnCover)
				yield return o => o.Screen.Cache = ScreenCacheMode.DestroyOnCover;
			if (op.Screen is { IsDialog: true, DialogResult: not null })
				yield return o => o.Screen.DialogResult = null;
		}
	}
}
