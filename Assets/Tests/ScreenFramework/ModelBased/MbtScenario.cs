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
		/// <summary>History.Edit。Current より下の行だけを無音編集する（top は維持）。遷移中に発行すると遅延適用される。</summary>
		Edit,
	}

	/// <summary>Edit op が適用する編集プリミティブ（Current より下の行に対して）。</summary>
	public enum MbtEditKind
	{
		/// <summary>下行リストの index 行を除去（生インスタンスがあれば無音 Unload）。</summary>
		RemoveAt,
		/// <summary>下行リストから TargetUid に一致する行を全除去（RemoveAll 経由）。</summary>
		RemoveByUid,
		/// <summary>下行リストの index に dormant 行を挿入（Pop で到達時にロード）。</summary>
		Insert,
		/// <summary>下行リストの index を別 Identifier へ差し替え（元の生インスタンスは破棄）。</summary>
		ReplaceAt,
		/// <summary>下行を全消去（top のみ残す）。</summary>
		Clear,
	}

	/// <summary>
	/// シナリオ全体の Effect 設定。EditMode では Effect prefab の InstantiateAsync が成立せず（PlayerLoop を
	/// 回さないので Yield が再開しない）、実体化経路に入ると停止する。よって踏めるのは
	/// 「Effect 解決の失敗が本筋を一切乱さない」C5 の resolution 耐性のみ。いずれの失敗モードでも effect=null に
	/// 落ち、遷移はモデルどおり（Effect 無し）に完走することを検証する。
	/// </summary>
	public enum MbtEffectMode
	{
		/// <summary>Registry を渡さない（既定。Effect 機構に一切触れない）。</summary>
		None,
		/// <summary>Registry.Resolve が例外を投げる。吸収されて effect=null（C5: Matcher 例外）。</summary>
		RegistryThrows,
		/// <summary>Registry が常にマッチ無しを返す。effect=null で素通し。</summary>
		NoMatch,
		/// <summary>Registry はマッチするが EffectRoot 未設定。警告ログ + effect=null（C5: EffectRoot 未設定）。</summary>
		RootMissing,
	}

	public enum MbtTokenMode
	{
		None,
		/// <summary>発行時点でキャンセル済みの ct を渡す。契約: 観測可能な効果を一切持たず OCE で即決着する。</summary>
		PreCanceled,
		/// <summary>発行直後（同期）に Cancel する。rollback ゾーンで滞留中なら OCE、commit ゾーン・Pop 系では無視。</summary>
		CancelAfterIssue,
	}

	/// <summary>
	/// in-flight 遷移を await 境界で停止させる位置。割り込み（preempt）/ 外部キャンセルは
	/// 「進行中の 1 本がどの境界で滞留しているか」でしか着弾できない（Run は遷移を直列化する）ため、
	/// 停止位置 = 撹乱の着弾点。rollback ゾーン（HoldLoad / HoldAfterLoad）の停止中に来た撹乱は OCE で
	/// 巻き戻し、commit ゾーン（HoldCommit / HoldAfterShow）の停止中に来た外部キャンセルは無視されて完走する。
	/// </summary>
	public enum MbtGateMode
	{
		None,
		/// <summary>OnInitialize を外部解放まで停止する（rollback ゾーンの最前段 = load 開始前）。</summary>
		HoldInitialize,
		/// <summary>handle.Load を外部解放まで停止する（rollback ゾーンの前段）。</summary>
		HoldLoad,
		/// <summary>OnAfterLoad を外部解放まで停止する（rollback ゾーンの最終境界 = commit へ移る直前）。</summary>
		HoldAfterLoad,
		/// <summary>OnBeforeShow を外部解放まで停止する（commit ゾーンの先頭境界）。</summary>
		HoldCommit,
		/// <summary>OnAfterShow を外部解放まで停止する（commit ゾーンの最終境界）。</summary>
		HoldAfterShow,
		/// <summary>
		/// Pop の退場 hook（OnBeforeHide）で停止する。退場は safeCt=None で走る commit ゾーンなので、
		/// 停止中の外部キャンセルは無視され、preempt も巻き戻せず完走を待つ。入場側ゲート（HoldCommit）とは
		/// 別コード（ExitPreviousAsync）の着弾点。
		/// </summary>
		HoldExit,
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
		/// <summary>OnBeforeShow（commit ゾーン）で投げる。吸収されて遷移は完走する契約。</summary>
		EnterHookThrows,
		/// <summary>OnAfterShow（commit ゾーンの後段 hook）で投げる。同じく吸収されて完走する契約。</summary>
		OnAfterShowThrows,
		/// <summary>PopTo の predicate で投げる。スタック無傷で伝播する契約。</summary>
		PredicateThrows,
	}

	/// <summary>画面（スペック）に紐づくフォールト。その画面のインスタンスがどの操作で触られても発火する。</summary>
	[Flags]
	public enum MbtScreenFaults
	{
		None = 0,
		BeforeHideThrows = 1,
		UnloadThrows = 2,
		AfterUnloadThrows = 4,
		/// <summary>復元ロード（2 回目以降の handle.Load）を常に失敗させる。Pop ではキャンセルされ top は退場しない。CloseTop では退場後に失敗し dormant top が残る（C10）。</summary>
		RestoreLoadFails = 8,
		/// <summary>OnResume（KeepOnCover から復帰する側）で投げる。Pop がキャンセルされ top は退場しない。</summary>
		ResumeThrows = 16,
		/// <summary>OnAfterHide（退場の commit ゾーン後段）で投げる。吸収されて退場は完遂する契約。</summary>
		AfterHideThrows = 32,
		/// <summary>OnSuspend（KeepOnCover で覆われる側）で投げる。吸収されて suspend は成立する契約。</summary>
		SuspendThrows = 64,
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
		/// <summary>Push 系・Edit(Insert/ReplaceAt) は操作ごとに固有の spec を持つ（uid で恒等）。</summary>
		public MbtScreenSpec Screen;
		/// <summary>PopTo / CloseAt / Edit(RemoveByUid) の対象画面 uid。-1 は「見つからない」ケース。</summary>
		public int TargetUid = -1;
		/// <summary>Edit op の編集プリミティブ。</summary>
		public MbtEditKind EditKind;
		/// <summary>Edit(RemoveAt/Insert/ReplaceAt) の下行リスト index（実行時に [0, 下行数] へ丸める）。</summary>
		public int EditIndex;

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
			if (Kind == MbtOpKind.Edit)
			{
				sb.Append(':').Append(EditKind);
				if (EditKind is MbtEditKind.RemoveAt or MbtEditKind.Insert or MbtEditKind.ReplaceAt)
					sb.Append("(idx=").Append(EditIndex).Append(')');
			}
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
		/// <summary>Page レイヤーの StackMode。Stack は覆っても下画面を残し（全 loaded 行が active）、blocker を敷く。</summary>
		public StackMode StackMode;
		/// <summary>Page レイヤーの Effect 設定。None 以外は resolution 失敗の耐性（C5）を踏む。</summary>
		public MbtEffectMode EffectMode;
		/// <summary>全 op 発行後（in-flight ゲート保持中）に ScreenNavigator.Shutdown を差し込むか（C9）。true のとき回復プローブは行わない。</summary>
		public bool ShutdownAtEnd;

		public MbtScenario Clone()
		{
			var c = new MbtScenario { Seed = Seed, StackMode = StackMode, EffectMode = EffectMode, ShutdownAtEnd = ShutdownAtEnd };
			foreach (var op in Ops) c.Ops.Add(op.Clone());
			return c;
		}

		public string Describe()
		{
			var sb = new StringBuilder();
			if (StackMode != StackMode.Cover) sb.Append("# StackMode=").AppendLine(StackMode.ToString());
			if (EffectMode != MbtEffectMode.None) sb.Append("# EffectMode=").AppendLine(EffectMode.ToString());
			if (ShutdownAtEnd) sb.AppendLine("# ShutdownAtEnd");
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
						< 14 => MbtGateMode.HoldInitialize,
						< 30 => MbtGateMode.HoldLoad,
						< 44 => MbtGateMode.HoldAfterLoad,
						< 57 => MbtGateMode.HoldCommit,
						< 65 => MbtGateMode.HoldAfterShow,
						_ => MbtGateMode.None,
					};
					if (rng.Next(100) < 25) op.Fault = PickPushFault(rng);
					// ロード前・ロード自体のフォールトとゲートは両立しない（ゲートに到達しない）
					if (op.Fault is MbtOpFault.ConfigureThrows or MbtOpFault.OnInitializeThrows
						or MbtOpFault.OnBeforeLoadThrows or MbtOpFault.LoadThrows or MbtOpFault.SpuriousOceOnBeforeLoad)
						op.Gate = MbtGateMode.None;
					// OnAfterLoad の例外はその hook で発火するので、それ以降に停止する gate には到達しない
					if (op.Fault == MbtOpFault.OnAfterLoadThrows
						&& op.Gate is MbtGateMode.HoldAfterLoad or MbtGateMode.HoldCommit or MbtGateMode.HoldAfterShow)
						op.Gate = MbtGateMode.None;
					// 同一 hook に gate と fault を同居させない（停止する hook 自身が throw すると停止できない）
					if (op.Fault == MbtOpFault.EnterHookThrows && op.Gate == MbtGateMode.HoldCommit)
						op.Gate = MbtGateMode.None;
					if (op.Fault == MbtOpFault.OnAfterShowThrows && op.Gate == MbtGateMode.HoldAfterShow)
						op.Gate = MbtGateMode.None;
				}
				else if (op.Kind == MbtOpKind.Edit)
				{
					// Edit は同期 void API（遷移チェーンに乗らない）。Token/Gate/Fault は無関係。
					// in-flight 中に発行されたら遅延適用される（= Overlap が即時/遅延を決める）。
					op.Token = MbtTokenMode.None;
					op.EditKind = (MbtEditKind)rng.Next(5);
					op.EditIndex = rng.Next(5);
					if (op.EditKind is MbtEditKind.Insert or MbtEditKind.ReplaceAt)
					{
						var spec = new MbtScreenSpec { Uid = nextUid++ };
						spec.Label = "S" + spec.Uid;
						op.Screen = spec;
						uidPool.Add(spec.Uid);   // 挿入/差し替え行は dormant で履歴に入るので PopTo の対象になり得る
					}
					else if (op.EditKind == MbtEditKind.RemoveByUid)
					{
						op.TargetUid = uidPool.Count > 0 && rng.Next(100) < 85 ? uidPool[rng.Next(uidPool.Count)] : -1;
					}
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
					// Pop の退場 hook で停止する commit ゾーンゲート。Configure 例外は退場前に決着するので両立しない。
					if (op.Kind == MbtOpKind.Pop && op.Fault == MbtOpFault.None && rng.Next(100) < 30)
						op.Gate = MbtGateMode.HoldExit;
				}
				sc.Ops.Add(op);
			}

			// シナリオ全体のフラグは op 生成の後で引く（既存シードの op 列を変えないため）。
			sc.StackMode = rng.Next(100) < 30 ? StackMode.Stack : StackMode.Cover;
			sc.EffectMode = rng.Next(100) switch
			{
				< 12 => MbtEffectMode.RegistryThrows,
				< 24 => MbtEffectMode.NoMatch,
				< 36 => MbtEffectMode.RootMissing,
				_ => MbtEffectMode.None,
			};
			sc.ShutdownAtEnd = rng.Next(100) < 14;
			return sc;
		}

		static MbtOpKind PickKind(Random rng) => rng.Next(100) switch
		{
			< 23 => MbtOpKind.Push,
			< 34 => MbtOpKind.PushAndAwait,
			< 48 => MbtOpKind.Pop,
			< 58 => MbtOpKind.PopTo,
			< 67 => MbtOpKind.Replace,
			< 74 => MbtOpKind.Change,
			< 80 => MbtOpKind.Reset,
			< 88 => MbtOpKind.CloseAt,
			< 93 => MbtOpKind.DismissAll,
			_ => MbtOpKind.Edit,
		};

		static MbtOpFault PickPushFault(Random rng) => rng.Next(8) switch
		{
			0 => MbtOpFault.ConfigureThrows,
			1 => MbtOpFault.OnInitializeThrows,
			2 => MbtOpFault.OnBeforeLoadThrows,
			3 => MbtOpFault.LoadThrows,
			4 => MbtOpFault.OnAfterLoadThrows,
			5 => MbtOpFault.SpuriousOceOnBeforeLoad,
			6 => MbtOpFault.EnterHookThrows,
			_ => MbtOpFault.OnAfterShowThrows,
		};

		static MbtScreenFaults PickScreenFault(Random rng) => rng.Next(7) switch
		{
			0 => MbtScreenFaults.BeforeHideThrows,
			1 => MbtScreenFaults.UnloadThrows,
			2 => MbtScreenFaults.AfterUnloadThrows,
			3 => MbtScreenFaults.RestoreLoadFails,
			4 => MbtScreenFaults.ResumeThrows,
			5 => MbtScreenFaults.AfterHideThrows,
			_ => MbtScreenFaults.SuspendThrows,
		};
	}
}
