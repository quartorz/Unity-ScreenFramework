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
		/// <summary>回復プローブを積む直前の、スタック各画面の表示状態（uid → active）。最上段かつ Loaded のみ active。</summary>
		public Dictionary<int, bool> PreProbeActiveByUid = new();
		/// <summary>回復プローブを積む直前の、敷かれている modal blocker GameObject の期待個数（Stack モードのみ非 0）。</summary>
		public int PreProbeBlockerCount;
		/// <summary>このシナリオが到達した「撹乱×ゾーン」分岐のタグ（カバレッジ計測用。docs の網羅表に対応）。</summary>
		public HashSet<string> CoverageTags = new();
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
				// 非 overlap = 直前までのチェーンを決着させてから発行する。チェーンが空になると
				// （実装の Run.finally と同じく）遅延された Edit がまとめて適用される。
				if (!plan.Overlap) { m.SettleAll(i); m.DrainDeferredEdits(); }
				if (plan.Kind == MbtOpKind.Edit)
				{
					m.IssueEdit(i);
				}
				else
				{
					m.Issue(i);
					if (plan.Token == MbtTokenMode.CancelAfterIssue) m.CancelToken(i);
				}
			}
			// Shutdown は in-flight ゲート保持中（解放前）に差し込む。preempt な DismissAll として
			// rollback ゾーンの遷移を巻き戻し、commit ゾーンの遷移は完走を待ってから全画面を畳む。
			if (sc.ShutdownAtEnd) m.IssueShutdown();
			m.SettleAll(m.IssuedCount);
			m.DrainDeferredEdits();
			var preProbeActive = m.BuildActiveMap();
			var preProbeBlockers = m.CountBlockers();
			MbtExpectation e;
			if (sc.ShutdownAtEnd)
			{
				// Shutdown は静的参照を畳む＝再 Initialize 必須なので、回復プローブは積まない。
				e = m.BuildExpectation(probe);
			}
			else
			{
				m.ApplyProbe(probe);
				e = m.BuildExpectation(probe);
			}
			e.PreProbeActiveByUid = preProbeActive;
			e.PreProbeBlockerCount = preProbeBlockers;
			return e;
		}

		enum OpState { NotIssued, Waiting, GatedInitialize, GatedLoad, GatedAfterLoad, GatedCommit, GatedExit, Settled }

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
			/// <summary>Stack モードで modal なこの行に input blocker GameObject が敷かれているか（下に行があるとき生成）。</summary>
			public bool HasBlocker;
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
			readonly HashSet<string> _tags = new();
			readonly List<MOp> _deferredEdits = new();
			readonly bool _isStack;
			MOp _chainTail;

			void Tag(string t) => _tags.Add(t);

			static string RollbackFaultTag(MbtOpFault fault) => fault switch
			{
				MbtOpFault.ConfigureThrows => MbtCoverage.RollbackFaultConfigure,
				MbtOpFault.OnInitializeThrows => MbtCoverage.RollbackFaultInitialize,
				MbtOpFault.OnBeforeLoadThrows => MbtCoverage.RollbackFaultBeforeLoad,
				_ => MbtCoverage.RollbackFaultLoad,
			};

			public Machine(MbtScenario sc)
			{
				_ops = sc.Ops.Select((p, i) => new MOp { Plan = p, Index = i }).ToList();
				_isStack = sc.StackMode == StackMode.Stack;
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
					Tag(MbtCoverage.PreCanceledNoop);
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
						if (victim == op || victim.State is OpState.Settled or OpState.NotIssued) continue;
						if (victim.State == OpState.GatedCommit) { Tag(MbtCoverage.PreemptSparesGatedCommit); continue; }
						if (victim.State == OpState.GatedExit) { Tag(MbtCoverage.PreemptSparesGatedExit); continue; }
						victim.CtsCanceled = true;
						if (victim.State == OpState.Waiting) Tag(MbtCoverage.PreemptKillsWaiting);
						if (victim.State == OpState.GatedInitialize) { Tag(MbtCoverage.PreemptKillsGatedInitialize); gatedVictim = victim; }
						if (victim.State == OpState.GatedLoad) { Tag(MbtCoverage.PreemptKillsGatedLoad); gatedVictim = victim; }
						if (victim.State == OpState.GatedAfterLoad) { Tag(MbtCoverage.PreemptKillsGatedAfterLoad); gatedVictim = victim; }
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
				if (op.State is OpState.GatedInitialize or OpState.GatedLoad or OpState.GatedAfterLoad)
				{
					Tag(op.State switch
					{
						OpState.GatedInitialize => MbtCoverage.CancelGatedInitialize,
						OpState.GatedLoad => MbtCoverage.CancelGatedLoad,
						_ => MbtCoverage.CancelGatedAfterLoad,
					});
					AddEnd(op.Plan.Kind, ok: false);
					SettleChain(op, MbtOutcome.Oce);
				}
				else if (op.State == OpState.GatedCommit)
				{
					Tag(op.Plan.Gate == MbtGateMode.HoldAfterEnter
						? MbtCoverage.CancelGatedAfterEnterIgnored
						: MbtCoverage.CancelGatedCommitIgnored);
				}
				else if (op.State == OpState.GatedExit)
				{
					Tag(MbtCoverage.CancelGatedExitIgnored);
				}
				// Waiting: 自分の番で OCE（TryRun でタグ付け）。GatedCommit: 外部 ct は commit ゾーンでは無視（完走）。
			}

			// ===== History.Edit =====

			bool ChainInFlight()
			{
				foreach (var o in _ops)
					if (o.State is OpState.Waiting or OpState.GatedInitialize or OpState.GatedLoad or OpState.GatedAfterLoad or OpState.GatedCommit or OpState.GatedExit)
						return true;
				return false;
			}

			/// <summary>発行済み op 数（合成 Shutdown op を含む）。最終 SettleAll の範囲に使う。</summary>
			public int IssuedCount => _ops.Count;

			/// <summary>
			/// ScreenNavigator.Shutdown 相当。捕捉済みレイヤーへの preempt な DismissAll として
			/// チェーン末尾に合成 op を積む。観測対象の op ではないので outcome は検査しない。
			/// </summary>
			public void IssueShutdown()
			{
				Tag(MbtCoverage.ShutdownFold);
				_ops.Add(new MOp
				{
					Plan = new MbtOp { Kind = MbtOpKind.DismissAll, Priority = InterruptPriority.Preempt, Overlap = true },
					Index = _ops.Count,
				});
				Issue(_ops.Count - 1);
			}

			public void IssueEdit(int i)
			{
				var op = _ops[i];
				// Edit は同期 void。チェーンには乗らず、呼び出し自体は常に成功扱い。
				op.State = OpState.Settled;
				op.ChainOutcome = MbtOutcome.Success;
				if (ChainInFlight())
				{
					// 遷移中の Edit は index 競合を避けるためチェーン完了まで遅延される。
					Tag(MbtCoverage.EditDeferred);
					_deferredEdits.Add(op);
				}
				else
				{
					Tag(MbtCoverage.EditImmediate);
					ApplyEdit(op);
				}
			}

			/// <summary>チェーンが空なら遅延 Edit を発行順に適用する（実装の DrainDeferredEdits 相当）。</summary>
			public void DrainDeferredEdits()
			{
				if (ChainInFlight() || _deferredEdits.Count == 0) return;
				var pending = _deferredEdits.ToList();
				_deferredEdits.Clear();
				foreach (var op in pending) ApplyEdit(op);
			}

			void ApplyEdit(MOp op)
			{
				// 空履歴への編集は適用されない（Current が無い状態で行を増やさない契約）。
				if (_stack.Count == 0) { Tag(MbtCoverage.EditEmptyNoop); return; }
				var belowCount = _stack.Count - 1;   // 編集対象は Current より下の行 [0, belowCount-1]
				switch (op.Plan.EditKind)
				{
					case MbtEditKind.RemoveAt:
						if (belowCount == 0) return;
						Tag(MbtCoverage.EditRemoveAt);
						RemoveBelowRow(Clamp(op.Plan.EditIndex, 0, belowCount - 1));
						break;
					case MbtEditKind.RemoveByUid:
						Tag(MbtCoverage.EditRemoveByUid);
						for (var k = belowCount - 1; k >= 0; k--)
							if (_stack[k].Spec.Uid == op.Plan.TargetUid) RemoveBelowRow(k);
						break;
					case MbtEditKind.Insert:
						Tag(MbtCoverage.EditInsert);
						_stack.Insert(Clamp(op.Plan.EditIndex, 0, belowCount), NewDormantRow(op.Plan.Screen));
						break;
					case MbtEditKind.ReplaceAt:
						if (belowCount == 0) return;
						Tag(MbtCoverage.EditReplaceAt);
						ReplaceBelowRow(Clamp(op.Plan.EditIndex, 0, belowCount - 1), op.Plan.Screen);
						break;
					case MbtEditKind.Clear:
						Tag(MbtCoverage.EditClear);
						for (var k = belowCount - 1; k >= 0; k--) RemoveBelowRow(k);
						break;
				}
			}

			void RemoveBelowRow(int idx)
			{
				var row = _stack[idx];
				if (row.Loaded)
				{
					// 履歴から外れた生インスタンスは Exit hook なしで Unload され、dialog awaiter は OCE になる。
					Tag(MbtCoverage.EditRemovedLiveEntry);
					SettleDialog(row, delivered: false);
				}
				row.Loaded = false;
				row.EntryAlive = false;
				_stack.RemoveAt(idx);
			}

			void ReplaceBelowRow(int idx, MbtScreenSpec newSpec)
			{
				var old = _stack[idx];
				if (old.Loaded)
				{
					Tag(MbtCoverage.EditRemovedLiveEntry);
					SettleDialog(old, delivered: false);
				}
				_stack[idx] = NewDormantRow(newSpec);
			}

			// Edit で挿入/差し替えされる行は dormant（インスタンスなし）で入り、IScreenEntry も持たない。
			static MRow NewDormantRow(MbtScreenSpec spec)
				=> new() { Spec = spec, Loaded = false, Suspended = false, EntryAlive = false };

			static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

			/// <summary>発行済み op のゲートを op 順に解放する（実行系の解放ループと同じ順序）。</summary>
			public void SettleAll(int issuedCount)
			{
				for (var i = 0; i < issuedCount; i++)
				{
					var op = _ops[i];
					if (op.GateReleased) continue;
					op.GateReleased = true;
					if (op.State == OpState.GatedInitialize) { Tag(MbtCoverage.GateInitializeReleased); ContinueAfterLoad(op); }
					else if (op.State == OpState.GatedLoad) { Tag(MbtCoverage.GateLoadReleased); ContinueAfterLoad(op); }
					else if (op.State == OpState.GatedAfterLoad) { Tag(MbtCoverage.GateAfterLoadReleased); ResumeAfterLoadGate(op); }
					else if (op.State == OpState.GatedCommit)
					{
						Tag(op.Plan.Gate == MbtGateMode.HoldAfterEnter
							? MbtCoverage.GateAfterEnterReleased : MbtCoverage.GateCommitReleased);
						FinishCommit(op);
					}
					else if (op.State == OpState.GatedExit)
					{
						Tag(MbtCoverage.GateExitReleased);
						var outcome = ApplyPopLike(op);
						AddEnd(op.Plan.Kind, ok: outcome == MbtOutcome.Success);
						SettleChain(op, outcome);
					}
					// Waiting / Settled: 解放だけ記録（後で body がゲートに来ても素通りする）
				}
			}

			/// <summary>各画面の表示状態（最上段かつ Loaded のみ active、覆われた・dormant は inactive）。</summary>
			public Dictionary<int, bool> BuildActiveMap()
			{
				var map = new Dictionary<int, bool>();
				for (var i = 0; i < _stack.Count; i++)
					// Cover は最上段の loaded だけ active。Stack は覆っても残るので loaded 行は全て active。
					map[_stack[i].Spec.Uid] = _stack[i].Loaded && (_isStack || i == _stack.Count - 1);
				return map;
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
					Tag(MbtCoverage.CancelWaiting);
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
							Tag(RollbackFaultTag(plan.Fault));
							AddEnd(plan.Kind, ok: false);
							SettleChain(op, MbtOutcome.Faulted);
							return;
						case MbtOpFault.SpuriousOceOnBeforeLoad:
							Tag(MbtCoverage.RollbackFaultSpuriousOce);
							AddEnd(plan.Kind, ok: false);
							SettleChain(op, MbtOutcome.Oce);
							return;
						// EnterHookThrows（OnBeforeEnter）/ OnAfterEnterThrows は commit ゾーンの hook。
						// ここで return せず素通りさせる = GuardedHook に吸収され遷移は完走する（C1）。
						case MbtOpFault.EnterHookThrows:
							Tag(MbtCoverage.CommitHookEnterAbsorbed);
							break;
						case MbtOpFault.OnAfterEnterThrows:
							Tag(MbtCoverage.CommitHookAfterEnterAbsorbed);
							break;
					}
					if (plan.Gate == MbtGateMode.HoldInitialize && !op.GateReleased)
					{
						op.State = OpState.GatedInitialize;
						return;
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
					// 退場 hook（OnBeforeExit）での停止は commit ゾーン。退場効果はまだ適用していないので、
					// 解放されるまでスタックは退場前のまま保たれる。外部キャンセルは無視され preempt も完走を待つ。
					if (plan.Kind == MbtOpKind.Pop && plan.Gate == MbtGateMode.HoldExit && !op.GateReleased)
					{
						op.State = OpState.GatedExit;
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
					Tag(MbtCoverage.RollbackFaultAfterLoad);
					AddEnd(plan.Kind, ok: false);
					SettleChain(op, MbtOutcome.Faulted);
					return;
				}
				// OnAfterLoad での停止は rollback ゾーンの最終境界。停止中の撹乱は OCE で巻き戻す。
				if (plan.Gate == MbtGateMode.HoldAfterLoad && !op.GateReleased)
				{
					op.State = OpState.GatedAfterLoad;
					return;
				}
				ApplyPushEffect(op);   // bookkeeping は Enter hook より前に確定する
				// OnBeforeEnter / OnAfterEnter での停止はどちらも commit ゾーン（外部キャンセルは無視され完走）。
				if ((plan.Gate is MbtGateMode.HoldCommit or MbtGateMode.HoldAfterEnter) && !op.GateReleased)
				{
					op.State = OpState.GatedCommit;
					return;
				}
				FinishCommit(op);
			}

			/// <summary>OnAfterLoad ゲート解放後。ロードは済んでいるので bookkeeping と commit だけ進める。</summary>
			void ResumeAfterLoadGate(MOp op)
			{
				ApplyPushEffect(op);
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
						var pushHadBelow = _stack.Count >= 1;
						AddNewRow(op);
						SetBlocker(_stack[^1], pushHadBelow);
						break;
					case MbtOpKind.Replace:
						if (_stack.Count == 0) { AddNewRow(op); break; }
						DestroyInstanceIfLoaded(_stack[^1]);
						var replaceHadBelow = _stack.Count >= 2;
						_stack[^1] = NewRow(op);
						SetBlocker(_stack[^1], replaceHadBelow);
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
						if (idx < _stack.Count - 2) Tag(MbtCoverage.PopToMiddleDiscard);
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
						Tag(MbtCoverage.CloseMiddle);
						var row = _stack[idx];
						SettleDialog(row, delivered: true);
						row.Loaded = false;
						row.Suspended = false;
						_stack.RemoveAt(idx);
						return MbtOutcome.Success;
					}
					case MbtOpKind.DismissAll:
						Tag(MbtCoverage.DismissAllNonEmpty);
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
					if ((below.Spec.Faults & MbtScreenFaults.RestoreLoadFails) != 0)
					{
						Tag(MbtCoverage.RestoreFaultDormantTop);
						return MbtOutcome.Faulted;
					}
					Tag(MbtCoverage.RestoreSuccess);
					below.Loaded = true;
					below.Suspended = false;
					below.EntryAlive = false;   // 復元は新インスタンス。元の IScreenEntry は死ぬ
					SetBlocker(below, _stack.Count >= 2);   // 復元画面も push 時と同じ規則で blocker 再構成（下に行があれば）
					return MbtOutcome.Success;
				}
				if (below.Suspended) { Tag(MbtCoverage.ResumeSuspended); below.Suspended = false; }   // OnResume の例外は吸収される
				return MbtOutcome.Success;
			}

			void SetBlocker(MRow row, bool hasBelow)
			{
				if (!_isStack || !hasBelow) return;   // blocker は Stack モードかつ下に行があるときだけ敷かれる（modal は既定 true）
				row.HasBlocker = true;
				Tag(MbtCoverage.StackBlockerCreated);
			}

			public int CountBlockers()
			{
				var n = 0;
				foreach (var row in _stack) if (row.HasBlocker) n++;
				return n;
			}

			void CoverTop()
			{
				// Stack モードは覆っても下画面を退場させない（suspend も destroy もせず loaded のまま残す）。
				if (_isStack) { if (_stack.Count > 0 && _stack[^1].Loaded) Tag(MbtCoverage.StackCoverNoExit); return; }
				if (_stack.Count == 0) return;
				var prev = _stack[^1];
				if (!prev.Loaded) return;   // dormant は退場フェーズなし
				if (prev.Spec.Cache == ScreenCacheMode.KeepOnCover)
				{
					Tag(MbtCoverage.KeepOnCoverSuspended);
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
				Tag(delivered ? MbtCoverage.DialogDelivered : MbtCoverage.DialogCanceled);
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
				// entry 捕捉 = その push 系 op が成立済み（成立前に発行された CloseAt は対象を掴めない）。
				// Edit の Insert/ReplaceAt も Screen を持つが IScreenEntry は返さないので push 系のみに絞る。
				var pushOp = _ops.FirstOrDefault(o => o.Plan.IsPushLike && o.Plan.Screen != null && o.Plan.Screen.Uid == uid);
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
					CoverageTags = _tags,
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
