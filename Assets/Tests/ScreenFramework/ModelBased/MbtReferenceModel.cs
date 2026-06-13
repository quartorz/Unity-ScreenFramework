using System.Collections.Generic;
using System.Linq;
using ScreenFramework;

namespace Tests.ScreenFramework.ModelBased
{
	public enum MbtOutcome
	{
		Success,
		Oce,
		Faulted,
		/// <summary>PushAndAwait の結果待ちが（仕様上）未決着のまま終わるケースにのみ許される。</summary>
		Pending,
	}

	public enum MbtDialogOutcome
	{
		NotApplicable,
		/// <summary>正常な閉じ方（最上段 Pop / Close / PopTo の最終 Pop / 中間 Close）で結果が配送される。</summary>
		Delivered,
		/// <summary>preempt / 差し替え / 全破棄 / cover-destroy 等で OCE 決着。</summary>
		Canceled,
		StillPending,
	}

	/// <summary>参照モデルが出力する「あるべき観測結果」。実行系（MbtExecutor）の観測と突き合わせる。</summary>
	public sealed class MbtExpectation
	{
		/// <summary>最終スタックのラベル列（下→上、回復プローブ含む）。</summary>
		public List<string> FinalStackLabels = new();
		/// <summary>op ごとの Task 決着。</summary>
		public MbtOutcome[] Outcomes;
		public MbtDialogOutcome[] DialogOutcomes;
		/// <summary>Delivered のときの期待結果テキスト（SetResult なしは null = default 配送）。</summary>
		public string[] DialogTexts;
		/// <summary>遷移イベント列（"Start:Push" / "End:Push:ok|fail"、プローブ含む）。</summary>
		public List<string> Events = new();
		/// <summary>uid → 最終的にインスタンスが生きているか（leak 検査用。プローブ含む）。</summary>
		public Dictionary<int, bool> FinalAliveByUid = new();
	}

	/// <summary>
	/// ScreenNavigatorImpl の「あるべき意味論」の参照実装。実装と独立にスタック・スケジューラ・
	/// PushAndAwait 決着・イベントを予言する。実装と食い違ったら、どちらかが契約違反
	/// （バグ or モデルの誤り）。契約の根拠は docs/FAULT-INJECTION.md の C1〜C10。
	///
	/// 重要な契約のエンコード位置:
	/// - 事前キャンセル済みトークンの操作は完全 no-op（他の遷移を巻き添えにしない）→ Issue() 冒頭
	/// - Preempt は rollback ゾーンの遷移だけを殺す（commit は完走）→ Issue() の victim 選別
	/// - rollback フォールト/キャンセルはスタック無傷で伝播、commit フォールトは吸収 → EnterBody/ApplyXxx
	/// - PushAndAwait は「正常な閉じ方」でのみ配送、それ以外は OCE、どの経路でもハングしない → SettleDialog
	/// - 復元ロード失敗は dormant top を残して伝播、以後の操作は成立 → RestoreOrResumeNewTop
	/// </summary>
	public static class MbtReferenceModel
	{
		public static MbtExpectation Evaluate(MbtScenario sc, MbtScreenSpec probe)
		{
			var m = new Machine(sc);
			for (var i = 0; i < sc.Ops.Count; i++)
			{
				var plan = sc.Ops[i];
				if (!plan.Overlap) m.SettleAll(i);
				m.Issue(i);
				if (plan.Token == MbtTokenMode.CancelAfterIssue) m.CancelToken(i);
			}
			m.SettleAll(sc.Ops.Count);
			m.ApplyProbe(probe);
			return m.BuildExpectation(probe);
		}

		enum OpState { NotIssued, Waiting, GatedLoad, GatedCommit, Settled }

		sealed class MRow
		{
			public MbtScreenSpec Spec;
			public bool Loaded = true;
			public bool Suspended;
			/// <summary>Push 系が返した IScreenEntry の presenter インスタンスが現役か（破棄→復元で偽になる）。</summary>
			public bool EntryAlive;
			public bool AwaiterSettled;
			public MbtDialogOutcome DialogOutcome = MbtDialogOutcome.NotApplicable;
			public string DialogText;
		}

		sealed class MOp
		{
			public MbtOp Plan;
			public int Index;
			public OpState State = OpState.NotIssued;
			public bool CtsCanceled;
			public bool GateReleased;
			/// <summary>遷移チェーンとしての決着（PushAndAwait は結果待ちと別）。</summary>
			public MbtOutcome ChainOutcome;
			public MOp Pred;
			/// <summary>PushAndAwait のとき、push 成立後に積んだ行。</summary>
			public MRow DialogRow;
		}

		sealed class Machine
		{
			readonly List<MRow> _stack = new();
			readonly List<MOp> _ops;
			readonly List<string> _events = new();
			MOp _chainTail;

			public Machine(MbtScenario sc)
			{
				_ops = sc.Ops.Select((p, i) => new MOp { Plan = p, Index = i }).ToList();
			}

			// ===== ドライバイベント =====

			public void Issue(int i)
			{
				var op = _ops[i];

				// CloseAt は Run の外で entry / Owns を同期評価する。entry 未捕捉（push 未成立）や
				// 対象インスタンスが既に現役でない場合は、トークン状態に関係なく即 no-op 成功。
				if (op.Plan.Kind == MbtOpKind.CloseAt && !OwnsAtIssue(op.Plan.TargetUid))
				{
					op.State = OpState.Settled;
					op.ChainOutcome = MbtOutcome.Success;
					return;
				}

				if (op.Plan.Token == MbtTokenMode.PreCanceled)
				{
					// 契約: 事前キャンセル済みの操作は、チェーンに参加せず・誰も巻き添えにせず・
					// イベントも発火せず、OCE で即決着する。
					op.State = OpState.Settled;
					op.ChainOutcome = MbtOutcome.Oce;
					return;
				}

				op.Pred = _chainTail;
				_chainTail = op;
				op.State = OpState.Waiting;

				if (op.Plan.Priority == InterruptPriority.Preempt)
				{
					// rollback ゾーン（待機中 / ロード滞留中）の先行遷移を全てキャンセルする。
					// commit ゾーンに入った遷移（GatedCommit）は完走する。
					// 実装は pending を新しい順にキャンセルするため、ロード滞留中の敗者（チェーン最古）が
					// 巻き戻る時点で待機中の敗者は全員キャンセル済み。モデルも「全員に印 → 滞留中を決着」の順にする。
					MOp gatedVictim = null;
					foreach (var victim in _ops)
					{
						if (victim == op || victim.State is OpState.Settled or OpState.NotIssued or OpState.GatedCommit) continue;
						victim.CtsCanceled = true;
						if (victim.State == OpState.GatedLoad) gatedVictim = victim;
					}
					if (gatedVictim != null)
					{
						// ロード滞留中の敗者は即 OCE 決着（補償 Unload はハンドル収支 P6 で検査）。
						// 決着の連鎖で待機中の敗者も順に OCE（イベントなし）になる。
						AddEnd(gatedVictim.Plan.Kind, ok: false);
						SettleChain(gatedVictim, MbtOutcome.Oce);
					}
				}

				if (op.Pred == null || op.Pred.State == OpState.Settled) TryRun(op);
			}

			public void CancelToken(int i)
			{
				var op = _ops[i];
				if (op.State == OpState.Settled) return;
				op.CtsCanceled = true;
				if (op.State == OpState.GatedLoad)
				{
					AddEnd(op.Plan.Kind, ok: false);
					SettleChain(op, MbtOutcome.Oce);
				}
				// Waiting: 自分の番で OCE。GatedCommit: 外部 ct は commit ゾーンでは無視（完走）。
			}

			/// <summary>発行済み op のゲートを op 順に解放する（実行系の解放ループと同じ順序）。</summary>
			public void SettleAll(int issuedCount)
			{
				for (var i = 0; i < issuedCount; i++)
				{
					var op = _ops[i];
					if (op.GateReleased) continue;
					op.GateReleased = true;
					if (op.State == OpState.GatedLoad) ContinueAfterLoad(op);
					else if (op.State == OpState.GatedCommit) FinishCommit(op);
					// Waiting / Settled: 解放だけ記録（後で body がゲートに来ても素通りする）
				}
			}

			public void ApplyProbe(MbtScreenSpec probe)
			{
				CoverTop();
				_stack.Add(new MRow { Spec = probe, EntryAlive = true });
				_events.Add("Start:Push");
				_events.Add("End:Push:ok");
			}

			// ===== チェーン進行 =====

			void TryRun(MOp op)
			{
				if (op.State != OpState.Waiting) return;
				if (op.CtsCanceled)
				{
					// prevDone 完了後・body 突入前の ThrowIfCancellationRequested で決着。イベントなし。
					SettleChain(op, MbtOutcome.Oce);
					return;
				}
				EnterBody(op);
			}

			void SettleChain(MOp op, MbtOutcome outcome)
			{
				op.State = OpState.Settled;
				op.ChainOutcome = outcome;
				var succ = _ops.FirstOrDefault(o => o.Pred == op && o.State == OpState.Waiting);
				if (succ != null) TryRun(succ);
			}

			void EnterBody(MOp op)
			{
				var plan = op.Plan;

				// イベント発火（FireStart）前の早期 return / 早期 throw
				switch (plan.Kind)
				{
					case MbtOpKind.Pop:
						if (_stack.Count <= 1) { SettleChain(op, MbtOutcome.Success); return; }
						break;
					case MbtOpKind.PopTo:
					{
						if (_stack.Count == 0) { SettleChain(op, MbtOutcome.Success); return; }
						if (plan.Fault == MbtOpFault.PredicateThrows) { SettleChain(op, MbtOutcome.Faulted); return; }
						var idx = FindTopmost(plan.TargetUid);
						if (idx < 0 || idx == _stack.Count - 1) { SettleChain(op, MbtOutcome.Success); return; }
						break;
					}
					case MbtOpKind.CloseAt:
					{
						// body 時点で対象インスタンスが現役でなければ no-op（発行後に死んだケース）
						if (FindAliveEntry(plan.TargetUid) < 0) { SettleChain(op, MbtOutcome.Success); return; }
						break;
					}
					case MbtOpKind.DismissAll:
						if (_stack.Count == 0) { SettleChain(op, MbtOutcome.Success); return; }
						break;
				}

				AddStart(plan.Kind);

				if (plan.IsPushLike)
				{
					switch (plan.Fault)
					{
						case MbtOpFault.ConfigureThrows:
						case MbtOpFault.OnInitializeThrows:
						case MbtOpFault.OnBeforeLoadThrows:
						case MbtOpFault.LoadThrows:
							AddEnd(plan.Kind, ok: false);
							SettleChain(op, MbtOutcome.Faulted);
							return;
						case MbtOpFault.SpuriousOceOnBeforeLoad:
							AddEnd(plan.Kind, ok: false);
							SettleChain(op, MbtOutcome.Oce);
							return;
						// EnterHookThrows（OnBeforeEnter）/ OnAfterEnterThrows は commit ゾーンの hook。
						// ここで return せず素通りさせる = GuardedHook に吸収され遷移は完走する（C1）。
					}
					if (plan.Gate == MbtGateMode.HoldLoad && !op.GateReleased)
					{
						op.State = OpState.GatedLoad;
						return;
					}
					ContinueAfterLoad(op);
				}
				else
				{
					if (plan.Fault == MbtOpFault.ConfigureThrows)
					{
						// ctx 構築（Configure 評価）は破棄を一切始める前。スタック無傷で伝播する。
						AddEnd(plan.Kind, ok: false);
						SettleChain(op, MbtOutcome.Faulted);
						return;
					}
					var outcome = ApplyPopLike(op);
					AddEnd(plan.Kind, ok: outcome == MbtOutcome.Success);
					SettleChain(op, outcome);
				}
			}

			/// <summary>rollback ゾーン（ロード）通過後。OnAfterLoad はまだ rollback、その先が commit。</summary>
			void ContinueAfterLoad(MOp op)
			{
				var plan = op.Plan;
				if (plan.Fault == MbtOpFault.OnAfterLoadThrows)
				{
					AddEnd(plan.Kind, ok: false);
					SettleChain(op, MbtOutcome.Faulted);
					return;
				}
				ApplyPushEffect(op);   // bookkeeping は Enter hook より前に確定する
				if (plan.Gate == MbtGateMode.HoldCommit && !op.GateReleased)
				{
					op.State = OpState.GatedCommit;
					return;
				}
				FinishCommit(op);
			}

			void FinishCommit(MOp op)
			{
				AddEnd(op.Plan.Kind, ok: true);
				SettleChain(op, MbtOutcome.Success);
			}

			// ===== スタック意味論 =====

			void ApplyPushEffect(MOp op)
			{
				var plan = op.Plan;
				switch (plan.Kind)
				{
					case MbtOpKind.Push:
					case MbtOpKind.PushAndAwait:
						CoverTop();
						AddNewRow(op);
						break;
					case MbtOpKind.Replace:
						if (_stack.Count == 0) { AddNewRow(op); break; }
						DestroyInstanceIfLoaded(_stack[^1]);
						_stack[^1] = NewRow(op);
						break;
					case MbtOpKind.Change:
						if (_stack.Count == 0) { AddNewRow(op); break; }
						for (var i = _stack.Count - 2; i >= 0; i--)
						{
							DestroyInstanceIfLoaded(_stack[i]);
							_stack.RemoveAt(i);
						}
						DestroyInstanceIfLoaded(_stack[^1]);
						_stack[^1] = NewRow(op);
						break;
					case MbtOpKind.Reset:
						for (var i = _stack.Count - 1; i >= 0; i--) DestroyInstanceIfLoaded(_stack[i]);
						_stack.Clear();
						_stack.Add(NewRow(op));
						break;
				}
			}

			MbtOutcome ApplyPopLike(MOp op)
			{
				var plan = op.Plan;
				switch (plan.Kind)
				{
					case MbtOpKind.Pop:
						return PopTopAndRestore();
					case MbtOpKind.PopTo:
					{
						var idx = FindTopmost(plan.TargetUid);
						// 中間は無音破棄（awaiter は OCE）
						for (var i = _stack.Count - 2; i > idx; i--)
						{
							DestroyInstanceIfLoaded(_stack[i]);
							_stack.RemoveAt(i);
						}
						// 最終段は通常 Pop 扱い（top の awaiter には結果配送）
						return PopTopAndRestore();
					}
					case MbtOpKind.CloseAt:
					{
						var idx = FindAliveEntry(plan.TargetUid);
						if (idx == _stack.Count - 1) return PopTopAndRestore();
						// 中間 Close は silent だが正常な閉じ方（退場 hook 経由）なので結果は配送される
						var row = _stack[idx];
						SettleDialog(row, delivered: true);
						row.Loaded = false;
						row.Suspended = false;
						_stack.RemoveAt(idx);
						return MbtOutcome.Success;
					}
					case MbtOpKind.DismissAll:
						for (var i = _stack.Count - 1; i >= 0; i--) DestroyInstanceIfLoaded(_stack[i]);
						_stack.Clear();
						return MbtOutcome.Success;
				}
				return MbtOutcome.Success;
			}

			MbtOutcome PopTopAndRestore()
			{
				var top = _stack[^1];
				SettleDialog(top, delivered: true);
				top.Loaded = false;
				top.Suspended = false;
				_stack.RemoveAt(_stack.Count - 1);
				if (_stack.Count == 0) return MbtOutcome.Success;   // CloseAt(top) は最後の 1 枚も閉じられる
				return RestoreOrResumeNewTop();
			}

			MbtOutcome RestoreOrResumeNewTop()
			{
				var below = _stack[^1];
				if (!below.Loaded)
				{
					// 復元ロード（完走必須ゾーン）。失敗は伝播するが履歴は巻き戻さず dormant top が残る（C10）。
					if ((below.Spec.Faults & MbtScreenFaults.RestoreLoadFails) != 0) return MbtOutcome.Faulted;
					below.Loaded = true;
					below.Suspended = false;
					below.EntryAlive = false;   // 復元は新インスタンス。元の IScreenEntry は死ぬ
					return MbtOutcome.Success;
				}
				if (below.Suspended) below.Suspended = false;   // OnResume の例外は吸収される
				return MbtOutcome.Success;
			}

			void CoverTop()
			{
				if (_stack.Count == 0) return;
				var prev = _stack[^1];
				if (!prev.Loaded) return;   // dormant は退場フェーズなし
				if (prev.Spec.Cache == ScreenCacheMode.KeepOnCover)
				{
					prev.Suspended = true;
				}
				else
				{
					// cover-destroy。覆われたダイアログの awaiter はここで OCE（仕様）
					DestroyInstance(prev);
				}
			}

			void AddNewRow(MOp op) => _stack.Add(NewRow(op));

			MRow NewRow(MOp op)
			{
				var row = new MRow
				{
					Spec = op.Plan.Screen,
					// PushAndAwait は IScreenEntry を返さないので CloseAt の対象にならない
					EntryAlive = op.Plan.Kind != MbtOpKind.PushAndAwait,
				};
				if (op.Plan.Kind == MbtOpKind.PushAndAwait)
				{
					row.DialogOutcome = MbtDialogOutcome.StillPending;
					op.DialogRow = row;
				}
				return row;
			}

			void DestroyInstanceIfLoaded(MRow row)
			{
				if (row.Loaded) DestroyInstance(row);
			}

			void DestroyInstance(MRow row)
			{
				row.Loaded = false;
				row.Suspended = false;
				row.EntryAlive = false;
				SettleDialog(row, delivered: false);
			}

			void SettleDialog(MRow row, bool delivered)
			{
				if (row.Spec is not { IsDialog: true } || row.AwaiterSettled) return;
				row.AwaiterSettled = true;
				row.DialogOutcome = delivered ? MbtDialogOutcome.Delivered : MbtDialogOutcome.Canceled;
				row.DialogText = delivered ? row.Spec.DialogResult : null;
			}

			// ===== 検索 =====

			int FindTopmost(int uid)
			{
				for (var i = _stack.Count - 1; i >= 0; i--)
				{
					if (_stack[i].Spec.Uid == uid) return i;
				}
				return -1;
			}

			int FindAliveEntry(int uid)
			{
				for (var i = _stack.Count - 1; i >= 0; i--)
				{
					if (_stack[i].Spec.Uid == uid && _stack[i].EntryAlive) return i;
				}
				return -1;
			}

			bool OwnsAtIssue(int uid)
			{
				// entry 捕捉 = その push 系 op が成立済み（成立前に発行された CloseAt は対象を掴めない）
				var pushOp = _ops.FirstOrDefault(o => o.Plan.Screen != null && o.Plan.Screen.Uid == uid);
				if (pushOp == null || pushOp.State != OpState.Settled || pushOp.ChainOutcome != MbtOutcome.Success) return false;
				if (pushOp.Plan.Kind == MbtOpKind.PushAndAwait) return false;
				return FindAliveEntry(uid) >= 0;
			}

			// ===== イベント =====

			void AddStart(MbtOpKind kind) => _events.Add("Start:" + KindName(kind));
			void AddEnd(MbtOpKind kind, bool ok) => _events.Add($"End:{KindName(kind)}:{(ok ? "ok" : "fail")}");

			static string KindName(MbtOpKind kind) => kind switch
			{
				MbtOpKind.PushAndAwait => "Push",
				MbtOpKind.CloseAt => "Close",
				_ => kind.ToString(),
			};

			// ===== 期待値の組み立て =====

			public MbtExpectation BuildExpectation(MbtScreenSpec probe)
			{
				var e = new MbtExpectation
				{
					Outcomes = new MbtOutcome[_ops.Count],
					DialogOutcomes = new MbtDialogOutcome[_ops.Count],
					DialogTexts = new string[_ops.Count],
					Events = _events,
				};
				foreach (var row in _stack) e.FinalStackLabels.Add(row.Spec.Label);

				foreach (var op in _ops)
				{
					if (op.Plan.Kind == MbtOpKind.PushAndAwait)
					{
						if (op.ChainOutcome != MbtOutcome.Success)
						{
							// 遷移自体が失敗/キャンセル → awaiter は同じ結末
							e.Outcomes[op.Index] = op.ChainOutcome;
							e.DialogOutcomes[op.Index] = op.ChainOutcome == MbtOutcome.Oce
								? MbtDialogOutcome.Canceled : MbtDialogOutcome.NotApplicable;
						}
						else
						{
							var d = op.DialogRow.DialogOutcome;
							e.DialogOutcomes[op.Index] = d;
							e.DialogTexts[op.Index] = op.DialogRow.DialogText;
							e.Outcomes[op.Index] = d switch
							{
								MbtDialogOutcome.Delivered => MbtOutcome.Success,
								MbtDialogOutcome.Canceled => MbtOutcome.Oce,
								_ => MbtOutcome.Pending,
							};
						}
					}
					else
					{
						e.Outcomes[op.Index] = op.ChainOutcome;
						e.DialogOutcomes[op.Index] = MbtDialogOutcome.NotApplicable;
					}
				}

				foreach (var op in _ops)
				{
					if (op.Plan.Screen != null)
						e.FinalAliveByUid[op.Plan.Screen.Uid] = false;
				}
				foreach (var row in _stack)
					e.FinalAliveByUid[row.Spec.Uid] = row.Loaded;
				return e;
			}
		}
	}
}
