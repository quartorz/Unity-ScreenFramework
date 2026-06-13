using System;
using System.Collections.Generic;
using System.Text;
using ScreenFramework;

namespace Tests.ScreenFramework.ModelBased
{
	/// <summary>
	/// モデルベーステストの語彙。シナリオ＝操作列で、各操作は
	/// 「種類 × 割り込み優先度 × トークン状態 × ゲート（await 境界での一時停止）× フォールト × 重ね打ち」
	/// のタプル。カバレッジの上限はこの語彙で決まる（docs/MODEL-BASED-TESTING.md 参照）。
	/// </summary>
	public enum MbtOpKind
	{
		Push,
		PushAndAwait,
		Pop,
		PopTo,
		Replace,
		Change,
		Reset,
		/// <summary>Push 系操作が返した IScreenEntry 経由の Close。対象が top なら top close、中間なら silent close。</summary>
		CloseAt,
		DismissAll,
	}

	public enum MbtTokenMode
	{
		None,
		/// <summary>発行時点でキャンセル済みの ct を渡す。契約: 観測可能な効果を一切持たず OCE で即決着する。</summary>
		PreCanceled,
		/// <summary>発行直後（同期）に Cancel する。rollback ゾーンで滞留中なら OCE、commit ゾーン・Pop 系では無視。</summary>
		CancelAfterIssue,
	}

	public enum MbtGateMode
	{
		None,
		/// <summary>handle.Load を外部解放まで停止する（rollback ゾーン滞留）。</summary>
		HoldLoad,
		/// <summary>OnBeforeEnter を外部解放まで停止する（commit ゾーン滞留）。</summary>
		HoldCommit,
	}

	public enum MbtOpFault
	{
		None,
		ConfigureThrows,
		OnInitializeThrows,
		OnBeforeLoadThrows,
		LoadThrows,
		OnAfterLoadThrows,
		/// <summary>ct 起因でない偽 OCE を OnBeforeLoad で投げる（rollback ゾーンでは OCE として伝播する契約）。</summary>
		SpuriousOceOnBeforeLoad,
		/// <summary>OnBeforeEnter（commit ゾーン）で投げる。吸収されて遷移は完走する契約。</summary>
		EnterHookThrows,
		/// <summary>PopTo の predicate で投げる。スタック無傷で伝播する契約。</summary>
		PredicateThrows,
	}

	/// <summary>画面（スペック）に紐づくフォールト。その画面のインスタンスがどの操作で触られても発火する。</summary>
	[Flags]
	public enum MbtScreenFaults
	{
		None = 0,
		BeforeExitThrows = 1,
		UnloadThrows = 2,
		AfterUnloadThrows = 4,
		/// <summary>復元ロード（2 回目以降の handle.Load）を常に失敗させる。dormant top 契約（C10）の入口。</summary>
		RestoreLoadFails = 8,
		ResumeThrows = 16,
	}

	public sealed class MbtScreenSpec
	{
		public int Uid;
		public string Label;
		public ScreenCacheMode Cache = ScreenCacheMode.DestroyOnCover;
		public MbtScreenFaults Faults;
		public bool IsDialog;
		/// <summary>非 null なら OnAfterLoad で SetResult する。null は「結果未書き込みで閉じる」ケース。</summary>
		public string DialogResult;

		public MbtScreenSpec Clone() => (MbtScreenSpec)MemberwiseClone();

		public string Describe()
		{
			var sb = new StringBuilder(Label);
			if (Cache == ScreenCacheMode.KeepOnCover) sb.Append(",Keep");
			if (Faults != MbtScreenFaults.None) sb.Append(",faults=").Append(Faults);
			if (IsDialog) sb.Append(",result=").Append(DialogResult ?? "(none)");
			return sb.ToString();
		}
	}

	public sealed class MbtOp
	{
		public MbtOpKind Kind;
		public InterruptPriority Priority;
		public MbtTokenMode Token;
		public MbtGateMode Gate;
		public MbtOpFault Fault;
		/// <summary>true: 直前までの操作群の決着を待たずに発行する（in-flight への重ね打ち）。</summary>
		public bool Overlap;
		/// <summary>Push 系のみ。操作ごとに固有の spec を持つ（uid で恒等）。</summary>
		public MbtScreenSpec Screen;
		/// <summary>PopTo / CloseAt の対象画面 uid。-1 は「見つからない」ケース。</summary>
		public int TargetUid = -1;

		public bool IsPushLike => Kind is MbtOpKind.Push or MbtOpKind.PushAndAwait or MbtOpKind.Replace or MbtOpKind.Change or MbtOpKind.Reset;

		public MbtOp Clone()
		{
			var c = (MbtOp)MemberwiseClone();
			c.Screen = Screen?.Clone();
			return c;
		}

		public string Describe(int index)
		{
			var sb = new StringBuilder();
			sb.Append('[').Append(index).Append("] ").Append(Kind);
			if (Screen != null) sb.Append('(').Append(Screen.Describe()).Append(')');
			if (TargetUid >= 0) sb.Append("(target=S").Append(TargetUid).Append(')');
			sb.Append(" prio=").Append(Priority);
			if (Token != MbtTokenMode.None) sb.Append(" token=").Append(Token);
			if (Gate != MbtGateMode.None) sb.Append(" gate=").Append(Gate);
			if (Fault != MbtOpFault.None) sb.Append(" fault=").Append(Fault);
			if (Overlap) sb.Append(" overlap");
			return sb.ToString();
		}
	}

	public sealed class MbtScenario
	{
		public int Seed;
		public List<MbtOp> Ops = new();

		public MbtScenario Clone()
		{
			var c = new MbtScenario { Seed = Seed };
			foreach (var op in Ops) c.Ops.Add(op.Clone());
			return c;
		}

		public string Describe()
		{
			var sb = new StringBuilder();
			for (var i = 0; i < Ops.Count; i++) sb.AppendLine(Ops[i].Describe(i));
			return sb.ToString();
		}
	}

	/// <summary>
	/// シード付きランダム生成器。「事前キャンセル済みトークン」「重ね打ち」「フォールト」は
	/// それぞれ独立した次元なので、その交点（例: in-flight 遷移 × 事前キャンセル済み Preempt）は
	/// 誰かが思いつかなくても確率的に必ず生成される。
	/// </summary>
	public static class MbtGenerator
	{
		public static MbtScenario Generate(int seed)
		{
			var rng = new Random(seed);
			var sc = new MbtScenario { Seed = seed };
			var uidPool = new List<int>();
			var nextUid = 1;
			var opCount = 3 + rng.Next(5);   // 3..7

			for (var i = 0; i < opCount; i++)
			{
				var op = new MbtOp
				{
					Kind = PickKind(rng),
					Overlap = i > 0 && rng.Next(100) < 45,
				};
				op.Priority = rng.Next(2) == 0 ? InterruptPriority.Preempt : InterruptPriority.Queue;
				op.Token = rng.Next(100) switch
				{
					< 10 => MbtTokenMode.PreCanceled,
					< 30 => MbtTokenMode.CancelAfterIssue,
					_ => MbtTokenMode.None,
				};

				if (op.IsPushLike)
				{
					var spec = new MbtScreenSpec
					{
						Uid = nextUid++,
						Cache = rng.Next(100) < 15 ? ScreenCacheMode.KeepOnCover : ScreenCacheMode.DestroyOnCover,
					};
					spec.Label = "S" + spec.Uid;
					if (rng.Next(100) < 25) spec.Faults = PickScreenFault(rng);
					if (op.Kind == MbtOpKind.PushAndAwait)
					{
						spec.IsDialog = true;
						spec.DialogResult = rng.Next(2) == 0 ? null : "R" + spec.Uid;
					}
					op.Screen = spec;
					uidPool.Add(spec.Uid);

					op.Gate = rng.Next(100) switch
					{
						< 30 => MbtGateMode.HoldLoad,
						< 45 => MbtGateMode.HoldCommit,
						_ => MbtGateMode.None,
					};
					if (rng.Next(100) < 25) op.Fault = PickPushFault(rng);
					// ロード前・ロード自体のフォールトとゲートは両立しない（ゲートに到達しない）
					if (op.Fault is MbtOpFault.ConfigureThrows or MbtOpFault.OnInitializeThrows
						or MbtOpFault.OnBeforeLoadThrows or MbtOpFault.LoadThrows or MbtOpFault.SpuriousOceOnBeforeLoad)
						op.Gate = MbtGateMode.None;
					// 同一 hook（OnBeforeEnter）への gate と fault の同居は避ける
					if (op.Fault == MbtOpFault.EnterHookThrows && op.Gate == MbtGateMode.HoldCommit)
						op.Gate = MbtGateMode.None;
				}
				else
				{
					if (op.Kind is MbtOpKind.PopTo or MbtOpKind.CloseAt)
						op.TargetUid = uidPool.Count > 0 && rng.Next(100) < 85 ? uidPool[rng.Next(uidPool.Count)] : -1;
					if (rng.Next(100) < 20)
						op.Fault = op.Kind == MbtOpKind.PopTo && rng.Next(2) == 0
							? MbtOpFault.PredicateThrows
							: MbtOpFault.ConfigureThrows;
					if (op.Kind == MbtOpKind.DismissAll)
					{
						op.Fault = MbtOpFault.None;             // DismissAll に Configure は無い
						op.Priority = InterruptPriority.Preempt; // 実 API 上、常に Preempt
					}
				}
				sc.Ops.Add(op);
			}
			return sc;
		}

		static MbtOpKind PickKind(Random rng) => rng.Next(100) switch
		{
			< 25 => MbtOpKind.Push,
			< 37 => MbtOpKind.PushAndAwait,
			< 52 => MbtOpKind.Pop,
			< 62 => MbtOpKind.PopTo,
			< 72 => MbtOpKind.Replace,
			< 80 => MbtOpKind.Change,
			< 86 => MbtOpKind.Reset,
			< 94 => MbtOpKind.CloseAt,
			_ => MbtOpKind.DismissAll,
		};

		static MbtOpFault PickPushFault(Random rng) => rng.Next(7) switch
		{
			0 => MbtOpFault.ConfigureThrows,
			1 => MbtOpFault.OnInitializeThrows,
			2 => MbtOpFault.OnBeforeLoadThrows,
			3 => MbtOpFault.LoadThrows,
			4 => MbtOpFault.OnAfterLoadThrows,
			5 => MbtOpFault.SpuriousOceOnBeforeLoad,
			_ => MbtOpFault.EnterHookThrows,
		};

		static MbtScreenFaults PickScreenFault(Random rng) => rng.Next(5) switch
		{
			0 => MbtScreenFaults.BeforeExitThrows,
			1 => MbtScreenFaults.UnloadThrows,
			2 => MbtScreenFaults.AfterUnloadThrows,
			3 => MbtScreenFaults.RestoreLoadFails,
			_ => MbtScreenFaults.ResumeThrows,
		};
	}
}
