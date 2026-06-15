using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;

namespace Tests.ScreenFramework.ModelBased
{
	using static ScreenTestFixtures;

	/// <summary>
	/// 注入フォールト（メッセージが "mbt: " を含む例外）の Debug.LogException だけをコンソールに
	/// 届く前に握り潰す ILogHandler。commit ゾーンの吸収契約は「ログに残して続行」なので、
	/// 注入フォールト 1 つごとに例外ログが出るのが正常動作だが、Unity Test Framework は
	/// Exception ログをテスト失敗として扱う（LogAssert.ignoreFailingMessages では抑止できなかった）。
	/// 生成テストでは LogAssert.Expect を 1 件ずつ並べることも不可能なので、ハンドラ層で濾す。
	/// "mbt: " を含まないログは素通しなので、予期しないエラーログは引き続きテストを落とす。
	/// </summary>
	internal sealed class MbtLogFilter : ILogHandler, IDisposable
	{
		readonly ILogHandler _inner;
		MbtLogFilter(ILogHandler inner) => _inner = inner;

		public static MbtLogFilter Install()
		{
			var filter = new MbtLogFilter(Debug.unityLogger.logHandler);
			Debug.unityLogger.logHandler = filter;
			return filter;
		}

		public void Dispose()
		{
			if (ReferenceEquals(Debug.unityLogger.logHandler, this))
				Debug.unityLogger.logHandler = _inner;
		}

		public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
			=> _inner.LogFormat(logType, context, format, args);

		public void LogException(Exception exception, UnityEngine.Object context)
		{
			for (var e = exception; e != null; e = e.InnerException)
			{
				if (e.Message != null && e.Message.Contains("mbt: ")) return;
			}
			_inner.LogException(exception, context);
		}
	}

	/// <summary>
	/// SetActive / SetParent を記録する観測用 view。同一 uid で複数回ロードされても（復元ロード）
	/// world.ViewByUid には最後に作られた＝現役のインスタンスが残るよう、生成時に上書き登録する。
	/// </summary>
	internal sealed class MbtView : IScreenViewInstance
	{
		public readonly int Uid;
		public bool Active;
		public bool HasParent;

		public MbtView(int uid, MbtWorld world)
		{
			Uid = uid;
			world.ViewByUid[uid] = this;
		}

		public void SetActive(bool active) => Active = active;
		public void SetParent(Transform parent) => HasParent = parent != null;
		public T As<T>() where T : class => null;
	}

	internal static class MbtGate
	{
		/// <summary>
		/// rollback ゾーンの hook で停止するためのゲート待ち。hook は本物の ct を受け取るので、
		/// 停止中に来た preempt / 外部キャンセルがゲートを解いて OCE を伝播させられるよう ct を登録する。
		/// gate が null なら停止しない。
		/// </summary>
		public static async UniTask AwaitRollback(UniTaskCompletionSource gate, CancellationToken ct)
		{
			if (gate == null) return;
			using (ct.Register(() => gate.TrySetCanceled(ct)))
				await gate.Task;
		}
	}

	public interface IMbtId
	{
		MbtScreenSpec Spec { get; }
	}

	internal sealed record MbtScreenId(MbtScreenSpec Spec, MbtWorld World) : ScreenIdentifier, IMbtId
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => World.CreateHandle(Spec);
		public override IScreenPresenter CreatePresenter(ScreenServices s) => World.CreatePresenter(Spec);
		public override ScreenCacheMode? CachePolicy => Spec.Cache;
	}

	internal sealed record MbtDialogId(MbtScreenSpec Spec, MbtWorld World) : ScreenIdentifier<EchoResult>, IMbtId
	{
		public override IScreenHandle CreateHandle(ScreenServices s) => World.CreateHandle(Spec);
		public override IScreenPresenter CreatePresenter(ScreenServices s) => World.CreatePresenter(Spec);
		public override ScreenCacheMode? CachePolicy => Spec.Cache;
	}

	/// <summary>1 シナリオ分の実行時状態。spec uid → 初回ロードのゲート/フォールトを引くための索引を持つ。</summary>
	internal sealed class MbtWorld
	{
		public readonly Dictionary<int, MbtOp> OpBySpecUid = new();
		public readonly Dictionary<int, MbtOpRuntime> RuntimeBySpecUid = new();
		public readonly Dictionary<int, int> InstanceCount = new();
		public readonly List<MbtHandle> Handles = new();
		public readonly Dictionary<int, IScreenEntry> Entries = new();
		public readonly Dictionary<int, MbtView> ViewByUid = new();
		public readonly List<string> Events = new();

		public IScreenPresenter CreatePresenter(MbtScreenSpec spec)
		{
			var count = (InstanceCount.TryGetValue(spec.Uid, out var c) ? c : 0) + 1;
			InstanceCount[spec.Uid] = count;
			var initial = count == 1;
			var op = initial && OpBySpecUid.TryGetValue(spec.Uid, out var o) ? o : null;
			var rt = initial && RuntimeBySpecUid.TryGetValue(spec.Uid, out var r) ? r : null;
			return spec.IsDialog
				? new MbtDialogPresenter(spec, op, rt)
				: new MbtPresenter(spec, op, rt);
		}

		public IScreenHandle CreateHandle(MbtScreenSpec spec)
		{
			// CreatePresenter → OnInitialize → CreateHandle の順なので、同一ロードサイクル内では
			// InstanceCount が既にこのサイクルの値になっている。1 = 初回（op のゲート/フォールトが効く）、
			// 2 以降 = 復元ロード（spec の RestoreLoadFails が効く）。
			var initial = InstanceCount.TryGetValue(spec.Uid, out var c) && c == 1;
			var op = initial && OpBySpecUid.TryGetValue(spec.Uid, out var o) ? o : null;
			var gate = initial && RuntimeBySpecUid.TryGetValue(spec.Uid, out var r) ? r.LoadGate : null;
			var handle = new MbtHandle(spec, op, gate, this);
			Handles.Add(handle);
			return handle;
		}
	}

	internal sealed class MbtOpRuntime
	{
		public MbtOp Plan;
		public CancellationTokenSource Cts;
		public UniTask<MbtObserved> Task;
		public UniTaskCompletionSource<IScreenViewInstance> LoadGate;
		public UniTaskCompletionSource AfterLoadGate;
		public UniTaskCompletionSource CommitGate;
		public UniTaskCompletionSource AfterEnterGate;
		public bool GatesReleased;
	}

	public sealed class MbtObserved
	{
		public MbtOutcome Outcome;
		public string DialogText;
		public string Error;
	}

	internal sealed class MbtHandle : IScreenHandle
	{
		readonly MbtScreenSpec _spec;
		readonly MbtOp _op;   // 初回ロードのみ非 null
		readonly UniTaskCompletionSource<IScreenViewInstance> _gate;
		readonly MbtWorld _world;

		public MbtScreenSpec Spec => _spec;
		public bool UnloadCalled { get; private set; }

		public MbtHandle(MbtScreenSpec spec, MbtOp op, UniTaskCompletionSource<IScreenViewInstance> gate, MbtWorld world)
		{
			_spec = spec;
			_op = op;
			_gate = gate;
			_world = world;
		}

		public async UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken ct)
		{
			if (_op != null)
			{
				if (_op.Fault == MbtOpFault.LoadThrows)
					throw new InvalidOperationException($"mbt: load fault ({_spec.Label})");
				if (_gate != null)
				{
					using (ct.Register(() => _gate.TrySetCanceled(ct)))
						return await _gate.Task;
				}
			}
			else if ((_spec.Faults & MbtScreenFaults.RestoreLoadFails) != 0)
			{
				throw new InvalidOperationException($"mbt: restore load fault ({_spec.Label})");
			}
			return new MbtView(_spec.Uid, _world);
		}

		public UniTask Unload(CancellationToken ct)
		{
			UnloadCalled = true;
			if ((_spec.Faults & MbtScreenFaults.UnloadThrows) != 0)
				throw new InvalidOperationException($"mbt: unload fault ({_spec.Label})");
			return UniTask.CompletedTask;
		}
	}

	internal sealed class MbtPresenter : IScreenPresenter
	{
		readonly MbtScreenSpec _spec;
		readonly MbtOp _op;   // 初回インスタンスのみ非 null
		readonly MbtOpRuntime _rt;

		public MbtPresenter(MbtScreenSpec spec, MbtOp op, MbtOpRuntime rt)
		{
			_spec = spec;
			_op = op;
			_rt = rt;
		}

		UniTask IScreenPresenter.OnInitialize(CancellationToken ct)
			=> _op?.Fault == MbtOpFault.OnInitializeThrows
				? throw new InvalidOperationException($"mbt: OnInitialize fault ({_spec.Label})")
				: UniTask.CompletedTask;

		UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken ct)
		{
			if (_op?.Fault == MbtOpFault.OnBeforeLoadThrows)
				throw new InvalidOperationException($"mbt: OnBeforeLoad fault ({_spec.Label})");
			if (_op?.Fault == MbtOpFault.SpuriousOceOnBeforeLoad)
				throw new OperationCanceledException($"mbt: spurious oce ({_spec.Label})");
			return UniTask.CompletedTask;
		}

		async UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance v, INavigationDataReader r, ITransitionContext x, CancellationToken ct)
		{
			if (_op?.Fault == MbtOpFault.OnAfterLoadThrows)
				throw new InvalidOperationException($"mbt: OnAfterLoad fault ({_spec.Label})");
			await MbtGate.AwaitRollback(_rt?.AfterLoadGate, ct);
		}

		UniTask IScreenPresenter.OnBeforeEnter(INavigationDataReader r, ITransitionContext x, CancellationToken ct)
		{
			if (_op?.Fault == MbtOpFault.EnterHookThrows)
				throw new InvalidOperationException($"mbt: OnBeforeEnter fault ({_spec.Label})");
			if (_rt?.CommitGate != null) return _rt.CommitGate.Task;
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken ct)
		{
			if (_op?.Fault == MbtOpFault.OnAfterEnterThrows)
				throw new InvalidOperationException($"mbt: OnAfterEnter fault ({_spec.Label})");
			if (_rt?.AfterEnterGate != null) return _rt.AfterEnterGate.Task;
			return UniTask.CompletedTask;
		}

		UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.BeforeExitThrows) != 0
				? throw new InvalidOperationException($"mbt: OnBeforeExit fault ({_spec.Label})")
				: UniTask.CompletedTask;

		UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext x, CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.AfterExitThrows) != 0
				? throw new InvalidOperationException($"mbt: OnAfterExit fault ({_spec.Label})")
				: UniTask.CompletedTask;

		UniTask IScreenPresenter.OnSuspend(CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.SuspendThrows) != 0
				? throw new InvalidOperationException($"mbt: OnSuspend fault ({_spec.Label})")
				: UniTask.CompletedTask;

		UniTask IScreenPresenter.OnResume(CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.ResumeThrows) != 0
				? throw new InvalidOperationException($"mbt: OnResume fault ({_spec.Label})")
				: UniTask.CompletedTask;

		UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.AfterUnloadThrows) != 0
				? throw new InvalidOperationException($"mbt: OnAfterUnload fault ({_spec.Label})")
				: UniTask.CompletedTask;
	}

	internal sealed class MbtDialogPresenter : DialogPresenter<object, object, EchoResult>
	{
		readonly MbtScreenSpec _spec;
		readonly MbtOp _op;
		readonly MbtOpRuntime _rt;

		public MbtDialogPresenter(MbtScreenSpec spec, MbtOp op, MbtOpRuntime rt)
		{
			_spec = spec;
			_op = op;
			_rt = rt;
		}

		protected override UniTask OnInitialize(CancellationToken ct)
			=> _op?.Fault == MbtOpFault.OnInitializeThrows
				? throw new InvalidOperationException($"mbt: OnInitialize fault ({_spec.Label})")
				: UniTask.CompletedTask;

		protected override UniTask OnBeforeLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			if (_op?.Fault == MbtOpFault.OnBeforeLoadThrows)
				throw new InvalidOperationException($"mbt: OnBeforeLoad fault ({_spec.Label})");
			if (_op?.Fault == MbtOpFault.SpuriousOceOnBeforeLoad)
				throw new OperationCanceledException($"mbt: spurious oce ({_spec.Label})");
			return UniTask.CompletedTask;
		}

		protected override async UniTask OnAfterLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			if (_spec.DialogResult != null) SetResult(new EchoResult { Text = _spec.DialogResult });
			if (_op?.Fault == MbtOpFault.OnAfterLoadThrows)
				throw new InvalidOperationException($"mbt: OnAfterLoad fault ({_spec.Label})");
			await MbtGate.AwaitRollback(_rt?.AfterLoadGate, ct);
		}

		protected override UniTask OnBeforeEnter(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			if (_op?.Fault == MbtOpFault.EnterHookThrows)
				throw new InvalidOperationException($"mbt: OnBeforeEnter fault ({_spec.Label})");
			if (_rt?.CommitGate != null) return _rt.CommitGate.Task;
			return UniTask.CompletedTask;
		}

		protected override UniTask OnAfterEnter(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			if (_op?.Fault == MbtOpFault.OnAfterEnterThrows)
				throw new InvalidOperationException($"mbt: OnAfterEnter fault ({_spec.Label})");
			if (_rt?.AfterEnterGate != null) return _rt.AfterEnterGate.Task;
			return UniTask.CompletedTask;
		}

		protected override UniTask OnBeforeExitCore(ITransitionContext ctx, CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.BeforeExitThrows) != 0
				? throw new InvalidOperationException($"mbt: OnBeforeExit fault ({_spec.Label})")
				: UniTask.CompletedTask;

		protected override UniTask OnAfterExit(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.AfterExitThrows) != 0
				? throw new InvalidOperationException($"mbt: OnAfterExit fault ({_spec.Label})")
				: UniTask.CompletedTask;

		protected override UniTask OnSuspend(CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.SuspendThrows) != 0
				? throw new InvalidOperationException($"mbt: OnSuspend fault ({_spec.Label})")
				: UniTask.CompletedTask;

		protected override UniTask OnResume(CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.ResumeThrows) != 0
				? throw new InvalidOperationException($"mbt: OnResume fault ({_spec.Label})")
				: UniTask.CompletedTask;

		protected override UniTask OnAfterUnloadCore(INavigationDataWriter writer, CancellationToken ct)
			=> (_spec.Faults & MbtScreenFaults.AfterUnloadThrows) != 0
				? throw new InvalidOperationException($"mbt: OnAfterUnload fault ({_spec.Label})")
				: UniTask.CompletedTask;
	}

	public sealed class MbtRunReport
	{
		public List<string> Failures = new();
		public bool Ok => Failures.Count == 0;
	}

	/// <summary>
	/// シナリオを実フレームワークに対して実行し、参照モデルの予言と突き合わせる。
	/// 全てのテストダブルは同期決着（ゲートは外部解放）なので、PlayerLoop を回さず
	/// EditMode で決定的に実行できる。pending タスクは await せず Status で観測する
	/// （ハングしているタスクを await するとテストランナーごと止まるため）。
	/// </summary>
	public static class MbtExecutor
	{
		const int ProbeUid = 999999;

		public static async System.Threading.Tasks.Task<MbtRunReport> Run(MbtScenario sc)
		{
			var report = new MbtRunReport();
			var world = new MbtWorld();
			var probeSpec = new MbtScreenSpec { Uid = ProbeUid, Label = "PROBE" };
			var expect = MbtReferenceModel.Evaluate(sc, probeSpec);

			// 注入フォールトの吸収ログ（仕様どおりの Debug.LogException）でテストが落ちないよう、
			// このラン中（teardown の DismissAll 含む）はハンドラ層で濾す。
			var logFilter = MbtLogFilter.Install();

			// 直前のラン/テストが静的参照を残していても安全に始められるよう防御的に畳む
			ScreenNavigator.Shutdown().Forget();

			var pageC = NewContainer("MbtPageRoot");
			var dialogC = NewContainer("MbtDialogRoot");
			var sysC = NewContainer("MbtSysRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(pageC),
				Dialog = NewLayer(dialogC),
				SystemDialog = NewLayer(sysC),
			});
			var nav = ScreenNavigator.Page;
			Action<ScreenTransitionEvent> onStart = e => world.Events.Add("Start:" + e.Kind);
			Action<ScreenTransitionEvent> onEnd = e => world.Events.Add($"End:{e.Kind}:{(e.Succeeded ? "ok" : "fail")}");
			nav.OnTransitionStart += onStart;
			nav.OnTransitionEnd += onEnd;

			var n = sc.Ops.Count;
			var rts = new MbtOpRuntime[n];
			try
			{
				for (var i = 0; i < n; i++)
				{
					var plan = sc.Ops[i];
					if (!plan.Overlap) ReleaseGates(rts, i, world);
					rts[i] = Issue(nav, world, plan);
					if (plan.Token == MbtTokenMode.CancelAfterIssue) rts[i].Cts?.Cancel();
				}
				ReleaseGates(rts, n, world);

				if (nav.IsTransitioning)
				{
					report.Failures.Add("P5: 全ゲート解放後も遷移チェーンが収束しない（ハングの疑い）");
					return report;   // probe を発行するとデッドロックするのでここで打ち切る
				}

				// P7 表示状態: プローブを積む前に、生きている各画面の active が「最上段かつ Loaded のみ true」
				// になっていること（覆われた・suspended・dormant は非表示）。プローブで全段が覆われる前に観測する。
				foreach (var kv in expect.PreProbeActiveByUid)
				{
					if (!world.ViewByUid.TryGetValue(kv.Key, out var view))
					{
						if (kv.Value)
							report.Failures.Add($"P7: S{kv.Key} は active のはずだが view が存在しない");
						continue;
					}
					if (view.Active != kv.Value)
						report.Failures.Add($"P7: S{kv.Key} の active={view.Active}（期待: {kv.Value}）");
				}

				// P5 回復プローブ（C8）: どんなシナリオの後でも次の操作が成立する。
				// Queue 優先度にして、万一残骸があっても preempt で隠蔽しない。
				var probeTask = WrapEntry(
					nav.Push(new MbtScreenId(probeSpec, world), new PushOptions { InterruptPriority = InterruptPriority.Queue }),
					world, ProbeUid).Preserve();
				if (probeTask.Status == UniTaskStatus.Pending)
				{
					report.Failures.Add("P5: 回復プローブの Push が決着しない");
				}
				else
				{
					var po = probeTask.GetAwaiter().GetResult();
					if (po.Outcome != MbtOutcome.Success)
						report.Failures.Add($"P5: 回復プローブの Push が失敗した（{po.Outcome} {po.Error}）");
				}
				if (nav.IsTransitioning)
					report.Failures.Add("P5: プローブ完了後も IsTransitioning が true のまま");

				// P2 / P4: 各 op の決着と PushAndAwait の配送
				for (var i = 0; i < n; i++)
				{
					var exp = expect.Outcomes[i];
					if (rts[i].Task.Status == UniTaskStatus.Pending)
					{
						if (exp != MbtOutcome.Pending)
							report.Failures.Add($"P2: op[{i}] {sc.Ops[i].Kind} が決着しない（期待: {exp}）");
						continue;
					}
					var ob = rts[i].Task.GetAwaiter().GetResult();
					if (exp == MbtOutcome.Pending)
					{
						report.Failures.Add($"P2: op[{i}] {sc.Ops[i].Kind} は結果待ちが続くはずだが {ob.Outcome} で決着した");
					}
					else if (ob.Outcome != exp)
					{
						report.Failures.Add($"P2: op[{i}] {sc.Ops[i].Kind} の決着が {ob.Outcome}（期待: {exp}）{ob.Error}");
					}
					else if (expect.DialogOutcomes[i] == MbtDialogOutcome.Delivered && ob.Outcome == MbtOutcome.Success
						&& !string.Equals(ob.DialogText, expect.DialogTexts[i]))
					{
						report.Failures.Add($"P4: op[{i}] の配送結果が \"{ob.DialogText}\"（期待: \"{expect.DialogTexts[i]}\"）");
					}
				}

				// P1: 最終スタック（プローブ含む）
				var actualStack = nav.History
					.Select(id => id is IMbtId m ? m.Spec.Label : id.ToString())
					.ToList();
				if (!actualStack.SequenceEqual(expect.FinalStackLabels))
					report.Failures.Add(
						$"P1: 最終スタック [{string.Join(", ", actualStack)}]（期待: [{string.Join(", ", expect.FinalStackLabels)}]）");

				// P9 Current 整合: Current は常に最上段の identifier。History の末尾と一致すること。
				var expectedTop = expect.FinalStackLabels.Count > 0 ? expect.FinalStackLabels[^1] : null;
				var actualCurrent = nav.Current is IMbtId cm ? cm.Spec.Label : nav.Current?.ToString();
				if (!string.Equals(actualCurrent, expectedTop))
					report.Failures.Add($"P9: Current が \"{actualCurrent}\"（期待: \"{expectedTop}\"）");

				// P3: 遷移イベント列
				if (!world.Events.SequenceEqual(expect.Events))
					report.Failures.Add(
						$"P3: イベント列の不一致\n      実際: {string.Join(" / ", world.Events)}\n      期待: {string.Join(" / ", expect.Events)}");

				// P6: ハンドル収支。生成された全ハンドルは、最終的に生きているインスタンスを除き Unload 済みであること
				// （補償漏れ = リーク、余計な Unload = 生きている画面の破壊）。
				foreach (var group in world.Handles.GroupBy(h => h.Spec.Uid))
				{
					var list = group.ToList();
					var alive = expect.FinalAliveByUid.TryGetValue(group.Key, out var a) && a;
					for (var k = 0; k < list.Count; k++)
					{
						var expectedUnloaded = !(k == list.Count - 1 && alive);
						if (list[k].UnloadCalled != expectedUnloaded)
							report.Failures.Add(
								$"P6: {list[k].Spec.Label} のハンドル #{k + 1}/{list.Count} の Unload={list[k].UnloadCalled}（期待: {expectedUnloaded}）");
					}
				}
			}
			finally
			{
				nav.OnTransitionStart -= onStart;
				nav.OnTransitionEnd -= onEnd;
				// Shutdown の DismissAll が残存スクリーンの注入フォールトを吸収ログするので、
				// フィルタはその後（最後）に戻す。残存画面は同期ダブルなので畳み込みも同期に決着する。
				// 後始末の Shutdown が例外/ハングで失敗するのも実装の不具合（壊れた状態で畳めない）なので
				// Forget で握り潰さず観測する。Status で見るのはハング時にテストランナーごと止めないため。
				try
				{
					var shutdown = ScreenNavigator.Shutdown();
					if (shutdown.Status == UniTaskStatus.Pending)
						report.Failures.Add("P0: teardown（Shutdown）が決着しない（後始末のハング）");
					else
						shutdown.GetAwaiter().GetResult();
				}
				catch (Exception e)
				{
					report.Failures.Add($"P0: teardown（Shutdown）が例外で失敗した {Describe(e)}");
				}
				DestroyContainer(pageC);
				DestroyContainer(dialogC);
				DestroyContainer(sysC);
				logFilter.Dispose();
			}
			return report;
		}

		/// <summary>発行済み op のゲートを op 順（= チェーン順）に解放する。参照モデルの SettleAll と同じ順序。</summary>
		static void ReleaseGates(MbtOpRuntime[] rts, int count, MbtWorld world)
		{
			for (var i = 0; i < count; i++)
			{
				var rt = rts[i];
				if (rt == null || rt.GatesReleased) continue;
				rt.GatesReleased = true;
				rt.LoadGate?.TrySetResult(new MbtView(rt.Plan.Screen.Uid, world));
				rt.AfterLoadGate?.TrySetResult();
				rt.CommitGate?.TrySetResult();
				rt.AfterEnterGate?.TrySetResult();
			}
		}

		static MbtOpRuntime Issue(IScreenNavigator nav, MbtWorld world, MbtOp plan)
		{
			var rt = new MbtOpRuntime { Plan = plan };
			var ct = CancellationToken.None;
			if (plan.Token != MbtTokenMode.None)
			{
				rt.Cts = new CancellationTokenSource();
				if (plan.Token == MbtTokenMode.PreCanceled) rt.Cts.Cancel();
				ct = rt.Cts.Token;
			}

			if (plan.IsPushLike)
			{
				if (plan.Gate == MbtGateMode.HoldLoad) rt.LoadGate = new UniTaskCompletionSource<IScreenViewInstance>();
				if (plan.Gate == MbtGateMode.HoldAfterLoad) rt.AfterLoadGate = new UniTaskCompletionSource();
				if (plan.Gate == MbtGateMode.HoldCommit) rt.CommitGate = new UniTaskCompletionSource();
				if (plan.Gate == MbtGateMode.HoldAfterEnter) rt.AfterEnterGate = new UniTaskCompletionSource();
				world.OpBySpecUid[plan.Screen.Uid] = plan;
				world.RuntimeBySpecUid[plan.Screen.Uid] = rt;
			}

			Action<INavigationDataWriter> configure = plan.Fault == MbtOpFault.ConfigureThrows
				? _ => throw new InvalidOperationException("mbt: configure fault")
				: null;

			switch (plan.Kind)
			{
				case MbtOpKind.Push:
					rt.Task = WrapEntry(nav.Push(new MbtScreenId(plan.Screen, world),
						new PushOptions { Configure = configure, InterruptPriority = plan.Priority }, ct), world, plan.Screen.Uid).Preserve();
					break;
				case MbtOpKind.PushAndAwait:
					rt.Task = WrapDialog(nav.PushAndAwait(new MbtDialogId(plan.Screen, world),
						new PushOptions { Configure = configure, InterruptPriority = plan.Priority }, ct)).Preserve();
					break;
				case MbtOpKind.Pop:
					rt.Task = WrapPlain(nav.Pop(
						new PopOptions { Configure = configure, InterruptPriority = plan.Priority }, ct)).Preserve();
					break;
				case MbtOpKind.PopTo:
				{
					var targetUid = plan.TargetUid;
					var predicateThrows = plan.Fault == MbtOpFault.PredicateThrows;
					rt.Task = WrapPlain(nav.PopTo(id =>
					{
						if (predicateThrows) throw new InvalidOperationException("mbt: predicate fault");
						return id is IMbtId m && m.Spec.Uid == targetUid;
					}, new PopToOptions { Configure = configure, InterruptPriority = plan.Priority }, ct)).Preserve();
					break;
				}
				case MbtOpKind.Replace:
					rt.Task = WrapEntry(nav.Replace(new MbtScreenId(plan.Screen, world),
						new ReplaceOptions { Configure = configure, InterruptPriority = plan.Priority }, ct), world, plan.Screen.Uid).Preserve();
					break;
				case MbtOpKind.Change:
					rt.Task = WrapEntry(nav.Change(new MbtScreenId(plan.Screen, world),
						new ChangeOptions { Configure = configure, InterruptPriority = plan.Priority }, ct), world, plan.Screen.Uid).Preserve();
					break;
				case MbtOpKind.Reset:
					rt.Task = WrapEntry(nav.Reset(new MbtScreenId(plan.Screen, world),
						new ResetOptions { Configure = configure, InterruptPriority = plan.Priority }, ct), world, plan.Screen.Uid).Preserve();
					break;
				case MbtOpKind.CloseAt:
					if (!world.Entries.TryGetValue(plan.TargetUid, out var entry) || entry == null)
					{
						// push 未成立で entry を掴めていない場合、呼び出しようがない = no-op。
						// 参照モデルの OwnsAtIssue=false（entry 未捕捉）に対応する。
						rt.Task = UniTask.FromResult(new MbtObserved { Outcome = MbtOutcome.Success }).Preserve();
						break;
					}
					rt.Task = WrapPlain(entry.Close(
						new PopOptions { Configure = configure, InterruptPriority = plan.Priority }, ct)).Preserve();
					break;
				case MbtOpKind.DismissAll:
					rt.Task = WrapPlain(nav.DismissAll(ct)).Preserve();
					break;
				case MbtOpKind.Edit:
					// History.Edit は同期 void。遷移中なら実装側がチェーン完了まで遅延適用する。
					// action の例外は即時パスのみここへ伝播する（遅延パスは実装が握り潰す）が、
					// 生成する action は index を [0, 下行数] に丸めるため throw しない。
					try
					{
						nav.History.Edit(BuildEditAction(world, plan));
						rt.Task = UniTask.FromResult(new MbtObserved { Outcome = MbtOutcome.Success }).Preserve();
					}
					catch (Exception e)
					{
						rt.Task = UniTask.FromResult(new MbtObserved { Outcome = MbtOutcome.Faulted, Error = Describe(e) }).Preserve();
					}
					break;
			}
			return rt;
		}

		static Action<IScreenHistoryEditor> BuildEditAction(MbtWorld world, MbtOp plan)
		{
			switch (plan.EditKind)
			{
				case MbtEditKind.RemoveAt:
					return e => { if (e.Stack.Count > 0) e.RemoveAt(Clamp(plan.EditIndex, 0, e.Stack.Count - 1)); };
				case MbtEditKind.RemoveByUid:
					return e => e.RemoveAll(id => id is IMbtId m && m.Spec.Uid == plan.TargetUid);
				case MbtEditKind.Insert:
					return e => e.Stack.Insert(Clamp(plan.EditIndex, 0, e.Stack.Count), new MbtScreenId(plan.Screen, world));
				case MbtEditKind.ReplaceAt:
					return e => { if (e.Stack.Count > 0) e.Stack[Clamp(plan.EditIndex, 0, e.Stack.Count - 1)] = new MbtScreenId(plan.Screen, world); };
				default:
					return e => e.Clear();
			}
		}

		static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

		static async UniTask<MbtObserved> WrapEntry(UniTask<IScreenEntry> task, MbtWorld world, int uid)
		{
			try
			{
				var entry = await task;
				if (entry != null) world.Entries[uid] = entry;
				return new MbtObserved { Outcome = MbtOutcome.Success };
			}
			catch (OperationCanceledException) { return new MbtObserved { Outcome = MbtOutcome.Oce }; }
			catch (Exception e) { return new MbtObserved { Outcome = MbtOutcome.Faulted, Error = Describe(e) }; }
		}

		static async UniTask<MbtObserved> WrapDialog(UniTask<EchoResult> task)
		{
			try
			{
				var result = await task;
				return new MbtObserved { Outcome = MbtOutcome.Success, DialogText = result?.Text };
			}
			catch (OperationCanceledException) { return new MbtObserved { Outcome = MbtOutcome.Oce }; }
			catch (Exception e) { return new MbtObserved { Outcome = MbtOutcome.Faulted, Error = Describe(e) }; }
		}

		static async UniTask<MbtObserved> WrapPlain(UniTask task)
		{
			try
			{
				await task;
				return new MbtObserved { Outcome = MbtOutcome.Success };
			}
			catch (OperationCanceledException) { return new MbtObserved { Outcome = MbtOutcome.Oce }; }
			catch (Exception e) { return new MbtObserved { Outcome = MbtOutcome.Faulted, Error = Describe(e) }; }
		}

		static string Describe(Exception e) => $"[{e.GetType().Name}: {e.Message}]";
	}
}
