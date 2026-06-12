using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.ScreenFramework
{
	using static ScreenTestFixtures;

	/// <summary>
	/// フォールトインジェクションテスト: 外部キャンセルと遷移の割り込み(Preempt / Queue)。操作横断の設計原則を扱う:
	/// 外部 CancellationToken は rollback ゾーンでのみ有効(OCE で伝播 + 完全クリーンアップ)で、
	/// 完走必須ゾーン(Pop の退場 / 復元ロード、commit 中の Push、DismissAll の退場)では無視される。
	/// 割り込みは負けた側を補償付きで OCE 決着させ(ハングしない)、勝者だけを残す。先行遷移の失敗は
	/// Queue した後続に引き継がれない。Push / Pop / Change / Replace / DismissAll を横断して検証する。
	/// commit ゾーンの例外は Debug.LogException されるので該当テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionCancelInterruptTests : FaultInjectionTestBase
	{
		// ===========================================================================
		// 外部キャンセル(rollback ゾーンで有効 / commit ゾーンで無視)
		// ===========================================================================

		[Test]
		public async Task Push_ExternalCancelDuringLoad_ThrowsOce_CleansUp_AndRecovers()
		{
			SetupNavigator();
			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var handle = new ControllableHandle(source);
			var presenter = new TrackingPresenter();
			var id = new ControllableScreenId(handle, () => presenter);
			using var cts = new CancellationTokenSource();

			var pushTask = ScreenNavigator.Page.Push(id, ct: cts.Token);
			cts.Cancel();

			Exception caught = null;
			try { await pushTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "rollback ゾーンのキャンセルは OCE で伝播する");
			Assert.IsTrue(handle.UnloadCalled, "キャンセルされた load も handle.Unload で補償される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "キャンセル時も OnAfterUnload を呼ぶ契約");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "キャンセルされた Push は履歴に残らない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "キャンセル後も次の Push が成立する");
		}

		[Test]
		public async Task Push_AlreadyCanceledToken_ThrowsOce_BeforeAnySideEffect()
		{
			SetupNavigator();
			var factoryInvoked = false;
			var id = new ControllableScreenId(new InstantHandle(), () =>
			{
				factoryInvoked = true;
				return new NullPresenter();
			});
			using var cts = new CancellationTokenSource();
			cts.Cancel();

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id, ct: cts.Token); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught);
			Assert.IsFalse(factoryInvoked, "キャンセル済みの ct なら presenter 生成すら始めない");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task Push_HookIgnoresCancellation_LoadedEntryIsStillDiscarded()
		{
			// OnBeforeLoad がキャンセルを発生させつつ自分は正常完了する(ct を観測しない行儀の悪い hook)。
			// hook が OCE を投げなくても、ロード済み entry は漏れずに巻き戻されて OCE が伝播する契約。
			SetupNavigator();
			using var cts = new CancellationTokenSource();
			var handle = new InstantHandle();
			var presenter = new CancelingPresenter("BeforeLoad", cts);
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id, ct: cts.Token); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught);
			Assert.IsTrue(handle.UnloadCalled, "hook が ct を無視して完走してもロード済み entry は補償 Unload される");
			CollectionAssert.Contains(presenter.Events, "AfterUnload", "破棄経路でも OnAfterUnload の補償 hook が呼ばれる");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task Push_CancelInCommitZone_IsIgnored_AndPushCompletes()
		{
			SetupNavigator();
			using var cts = new CancellationTokenSource();
			var gated = new GatedPresenter();
			var id = new ControllableScreenId(new InstantHandle(), () => gated);

			var pushTask = ScreenNavigator.Page.Push(id, ct: cts.Token);
			await gated.Started;   // OnBeforeEnter = 完走必須ゾーンに入っている
			cts.Cancel();
			gated.Release();

			await pushTask;   // OCE にならず完走する

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(id, ScreenNavigator.Page.Current, "完走必須ゾーンに入った遷移は外部キャンセルで止まらない");
		}

		[Test]
		public async Task Pop_CancelDuringExit_IsIgnored_AndPopCompletes()
		{
			// Pop は全段が完走必須ゾーン。退場中にキャンセルされても完走する。
			SetupNavigator();
			using var cts = new CancellationTokenSource();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			var presenterB = new CancelingPresenter("BeforeExit", cts);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => presenterB));

			await ScreenNavigator.Page.Pop(ct: cts.Token);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
			CollectionAssert.Contains(presenterB.Events, "AfterUnload", "キャンセル後も退場シーケンスは最後まで進む");
		}

		[Test]
		public async Task Push_CanceledEntryDiscardUnloadFails_OceStillPropagates_AndCompensationContinues()
		{
			// ct を観測しない hook が完走 → ロード済み entry の破棄(discard)経路で Unload が落ちるケース。
			// 破棄経路の例外はログに留まり、OnAfterUnload までの補償は続き、OCE が伝播する。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at handle\\.Unload"));
			using var cts = new CancellationTokenSource();
			var handle = new FaultyUnloadHandle();
			var presenter = new CancelOnBeforeLoadPresenter(cts);
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id, ct: cts.Token); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "破棄処理の失敗で OCE がすり替わらない");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "Unload の失敗後も OnAfterUnload の補償まで進む");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "二重フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task Pop_ExternalCancelDuringRestoreLoad_IsIgnored_AndPopCompletes()
		{
			SetupNavigator(); // DestroyOnCover: 覆われた A は Pop 時に再ロードされる
			using var cts = new CancellationTokenSource();
			var handle = new SecondLoadControllableHandle();
			var idA = new ControllableScreenId(handle);
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			var popTask = ScreenNavigator.Page.Pop(ct: cts.Token);
			await handle.SecondLoadStarted;   // 復元ロード(完走必須ゾーン)の途中
			cts.Cancel();
			handle.CompleteSecondLoad();

			await popTask;   // OCE にならず完走する

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "復元ロード中のキャンセルは無視されて Pop が完走する");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		[Test]
		public async Task Push_WaitForStageCanceledExternally_RollsBackWithOce()
		{
			// timeout なしの WaitForStage が外部キャンセルで OCE 決着し、遷移は補償付きで巻き戻る。
			SetupNavigator();
			using var cts = new CancellationTokenSource();
			var handle = new InstantHandle();
			var presenter = new StageWaitCancelPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			var pushTask = ScreenNavigator.Page.Push(id, ct: cts.Token);
			cts.Cancel();

			Exception caught = null;
			try { await pushTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "stage 待ちの外部キャンセルは OCE で決着する(ハングしない)");
			Assert.IsTrue(handle.UnloadCalled, "キャンセルされた遷移も補償 Unload される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "破棄経路でも OnAfterUnload が呼ばれる");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "キャンセル後も次の Push が成立する");
		}

		[Test]
		public async Task Change_ExternalCancelDuringLoad_RollsBack_AndStackSurvives()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);

			using var cts = new CancellationTokenSource();
			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var handle = new ControllableHandle(source);
			var presenter = new TrackingPresenter();
			var idX = new ControllableScreenId(handle, () => presenter);

			var changeTask = ScreenNavigator.Page.Change(idX, ct: cts.Token);
			cts.Cancel();

			Exception caught = null;
			try { await changeTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "Change のロード中キャンセルは OCE で決着する");
			Assert.IsTrue(handle.UnloadCalled, "キャンセルされたロードは補償 Unload される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "破棄経路でも OnAfterUnload が呼ばれる");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "キャンセルされた Change は下スタックも壊さない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "旧最上段が Current のまま生き残る");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Change(idC);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "キャンセル後も Change を再試行できる");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task DismissAll_ExternalCancelDuringExit_IsIgnored_AndCompletes()
		{
			// DismissAll は Pop 系と同じく全段完走必須ゾーン。退場 hook の途中で外部キャンセル
			// されても完走し、畳み残しを生まない。
			SetupNavigator();
			using var cts = new CancellationTokenSource();
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var gated = new GatedExitPresenter();
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => gated));

			var dismissTask = ScreenNavigator.Page.DismissAll(cts.Token);
			await gated.Started;   // 最上段の OnBeforeExit で停止中
			cts.Cancel();
			gated.Release();

			await dismissTask;   // OCE にならず完走する

			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "退場中のキャンセルは無視されて全画面が畳まれる");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "空スタックからの再 Push が成立する");
		}

		// ===========================================================================
		// 割り込み(Preempt / Queue)
		// ===========================================================================

		[Test]
		public async Task Preempt_DuringLoad_LoserIsRolledBack_WinnerWins()
		{
			SetupNavigator();
			var sourceA = new UniTaskCompletionSource<IScreenViewInstance>();
			var handleA = new ControllableHandle(sourceA);
			var presenterA = new TrackingPresenter();
			var idA = new ControllableScreenId(handleA, () => presenterA);
			var idB = new MarkerScreenId("B");

			var pushA = ScreenNavigator.Page.Push(idA);
			var pushB = ScreenNavigator.Page.Push(idB);   // Preempt 既定: load 中の A を殺す

			Exception caughtA = null;
			try { await pushA; }
			catch (Exception e) { caughtA = e; }
			await pushB;

			Assert.IsInstanceOf<OperationCanceledException>(caughtA, "preempt された側は OCE で決着する");
			Assert.IsTrue(handleA.UnloadCalled, "preempt された load は handle.Unload で補償される");
			Assert.IsTrue(presenterA.OnAfterUnloadCalled);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "負けた Push は履歴に残らない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Preempt_ArrivingInCommitZone_DoesNotCancel_CurrentTransitionCompletes()
		{
			SetupNavigator();
			var gatedA = new GatedPresenter();
			var idA = new ControllableScreenId(new InstantHandle(), () => gatedA);
			var idB = new MarkerScreenId("B");

			var pushA = ScreenNavigator.Page.Push(idA);
			await gatedA.Started;   // A は完走必須ゾーン(OnBeforeEnter)で停止中
			var pushB = ScreenNavigator.Page.Push(idB);   // preempt は来るが commit ゾーンには効かない
			gatedA.Release();

			await pushA;   // A は OCE にならず完走する
			await pushB;

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "commit ゾーンの遷移は巻き戻されず、両方とも積まれる");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Queue_AfterFaultedOperation_StillRuns()
		{
			// FIFO チェーンは先行遷移の失敗を後続に引き継がない(先行のエラーは握り潰して自分の番を実行する)。
			SetupNavigator();
			var sourceA = new UniTaskCompletionSource<IScreenViewInstance>();
			var idA = new ControllableScreenId(new ControllableHandle(sourceA));
			var idB = new MarkerScreenId("B");

			var pushA = ScreenNavigator.Page.Push(idA);
			var pushB = ScreenNavigator.Page.Push(idB, new PushOptions { InterruptPriority = InterruptPriority.Queue });
			sourceA.TrySetException(new InvalidOperationException("fault injected at queued load"));

			Exception caughtA = null;
			try { await pushA; }
			catch (Exception e) { caughtA = e; }
			await pushB;

			Assert.IsInstanceOf<InvalidOperationException>(caughtA);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "先行遷移の失敗が Queue した後続を巻き込まない");
		}

		[Test]
		public async Task Preempt_Chain_BothLosersSettleWithOce_LastWinnerWins()
		{
			// 二重割り込み: load 中の A を B が殺し、待機中の B を C が殺す。
			// 敗者は両方 OCE で決着し(ハングしない)、最後の勝者だけが積まれる。
			SetupNavigator();
			var sourceA = new UniTaskCompletionSource<IScreenViewInstance>();
			var handleA = new ControllableHandle(sourceA);
			var idA = new ControllableScreenId(handleA);
			var sourceB = new UniTaskCompletionSource<IScreenViewInstance>();
			var idB = new ControllableScreenId(new ControllableHandle(sourceB));
			var idC = new MarkerScreenId("C");

			var pushA = ScreenNavigator.Page.Push(idA);
			var pushB = ScreenNavigator.Page.Push(idB);
			var pushC = ScreenNavigator.Page.Push(idC);

			Exception caughtA = null;
			Exception caughtB = null;
			try { await pushA; }
			catch (Exception e) { caughtA = e; }
			try { await pushB; }
			catch (Exception e) { caughtB = e; }
			await pushC;

			Assert.IsInstanceOf<OperationCanceledException>(caughtA, "load 中に殺された A は OCE で決着する");
			Assert.IsInstanceOf<OperationCanceledException>(caughtB, "開始前に殺された B も OCE で決着する");
			Assert.IsTrue(handleA.UnloadCalled, "load 中だった A は補償 Unload される");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "敗者は履歴に残らない");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Preempt_DuringOnBeforeLoadHook_LoserIsCompensated()
		{
			// 割り込み点が presenter hook(OnBeforeLoad)の await 境界に刺さるケース。hook は ct を正しく観測する行儀の良い実装。
			SetupNavigator();
			var handle = new InstantHandle();
			var presenter = new HangingBeforeLoadPresenter();
			var idA = new ControllableScreenId(handle, () => presenter);
			var idB = new MarkerScreenId("B");

			var pushA = ScreenNavigator.Page.Push(idA);
			var pushB = ScreenNavigator.Page.Push(idB);

			Exception caughtA = null;
			try { await pushA; }
			catch (Exception e) { caughtA = e; }
			await pushB;

			Assert.IsInstanceOf<OperationCanceledException>(caughtA, "hook 境界で殺された側も OCE で決着する");
			Assert.IsTrue(handle.UnloadCalled, "ロード済み view は補償 Unload される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "破棄経路でも OnAfterUnload が呼ばれる");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Pop_PreemptArrivesDuringExit_PopStillCompletes()
		{
			// Pop は全段が完走必須ゾーン。退場 hook の途中に Preempt が来ても Pop は完走し、
			// 割り込みはその後に実行される。
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			var gated = new GatedExitPresenter();
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => gated));

			var popTask = ScreenNavigator.Page.Pop();
			await gated.Started;   // Pop は OnBeforeExit で停止中
			var idC = new MarkerScreenId("C");
			var pushC = ScreenNavigator.Page.Push(idC);   // Preempt 既定だが commit 中の Pop は殺せない
			gated.Release();

			await popTask;   // OCE にならず完走する
			await pushC;

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "Pop 完了後に割り込みの Push が積まれる");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Queue_WaitingOpCanceledExternally_SettlesOce_WithoutSideEffects()
		{
			SetupNavigator();
			using var cts = new CancellationTokenSource();
			var gatedA = new GatedPresenter();
			var idA = new ControllableScreenId(new InstantHandle(), () => gatedA);
			var factoryInvoked = false;
			var idB = new ControllableScreenId(new InstantHandle(), () =>
			{
				factoryInvoked = true;
				return new NullPresenter();
			});

			var pushA = ScreenNavigator.Page.Push(idA);
			await gatedA.Started;   // A は完走必須ゾーンで停止中
			var pushB = ScreenNavigator.Page.Push(idB, new PushOptions { InterruptPriority = InterruptPriority.Queue }, cts.Token);
			cts.Cancel();   // B は A の完了待ち(キュー待機中)にキャンセルされる
			gatedA.Release();

			await pushA;   // 先行 A は巻き込まれず完走する
			Exception caughtB = null;
			try { await pushB; }
			catch (Exception e) { caughtB = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caughtB, "待機中にキャンセルされた遷移は OCE で決着する");
			Assert.IsFalse(factoryInvoked, "実行前にキャンセルされた遷移は副作用を持たない");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "先行 A だけが積まれる");
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "キャンセル後も次の Push が成立する");
		}

		[Test]
		public async Task Replace_PreemptedDuringLoad_LoserCompensated_AndOldScreenSurvives()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);

			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var handle = new ControllableHandle(source);
			var presenter = new TrackingPresenter();
			var idX = new ControllableScreenId(handle, () => presenter);
			var idB = new MarkerScreenId("B");

			var replaceTask = ScreenNavigator.Page.Replace(idX);
			var pushB = ScreenNavigator.Page.Push(idB);   // Preempt 既定: ロード中の Replace を殺す

			Exception caught = null;
			try { await replaceTask; }
			catch (Exception e) { caught = e; }
			await pushB;

			Assert.IsInstanceOf<OperationCanceledException>(caught, "preempt された Replace は OCE で決着する");
			Assert.IsTrue(handle.UnloadCalled, "preempt されたロードは補償 Unload される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled);
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "差し替え前の A は生き残り、勝者 B が積まれる");
			Assert.AreSame(idA, ScreenNavigator.Page.History[0], "失敗した Replace は旧画面を壊さない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task DismissAll_PreemptsLoadingPush_LoserSettlesOce_AndStackIsCleared()
		{
			// DismissAll は常に Preempt 発行。ロード中(rollback ゾーン)の Push は殺されて
			// 補償され、その後スタックが空まで畳まれる。
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);

			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var handle = new ControllableHandle(source);
			var presenter = new TrackingPresenter();
			var pushX = ScreenNavigator.Page.Push(new ControllableScreenId(handle, () => presenter));

			var dismissTask = ScreenNavigator.Page.DismissAll();

			Exception caught = null;
			try { await pushX; }
			catch (Exception e) { caught = e; }
			await dismissTask;

			Assert.IsInstanceOf<OperationCanceledException>(caught, "DismissAll に殺されたロード中の Push は OCE で決着する");
			Assert.IsTrue(handle.UnloadCalled, "殺されたロードは補償 Unload される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled);
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "敗者は積まれず、既存スタックも全て畳まれる");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "DismissAll 後も次の Push が成立する");
		}

		[Test]
		public async Task Preempt_QueuedOpNeverStarts_FiresNeitherStartNorEnd()
		{
			// 遷移イベントは「開始した遷移」に対してだけ Start / End が対で発火する。
			// 開始前に preempt で消えた待機中の遷移は、どちらのイベントも出さない(片割れイベントが
			// 観測側のカウントを狂わせない)。開始済みでキャンセルされた遷移は End(Succeeded=false) まで出る。
			SetupNavigator();
			var starts = new List<ScreenTransitionEvent>();
			var ends = new List<ScreenTransitionEvent>();
			ScreenNavigator.Page.OnTransitionStart += e => starts.Add(e);
			ScreenNavigator.Page.OnTransitionEnd += e => ends.Add(e);

			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var idA = new ControllableScreenId(new ControllableHandle(source));
			var idB = new MarkerScreenId("B");
			var idC = new MarkerScreenId("C");

			var pushA = ScreenNavigator.Page.Push(idA);   // 実行中(ロード待ち)になる
			var pushB = ScreenNavigator.Page.Push(idB, new PushOptions { InterruptPriority = InterruptPriority.Queue });   // 待機
			var pushC = ScreenNavigator.Page.Push(idC);   // preempt: A(実行中)と B(未開始)を両方キャンセル

			Exception caughtA = null, caughtB = null;
			try { await pushA; } catch (Exception e) { caughtA = e; }
			try { await pushB; } catch (Exception e) { caughtB = e; }
			await pushC;

			Assert.IsInstanceOf<OperationCanceledException>(caughtA, "実行中だった A は OCE で決着する");
			Assert.IsInstanceOf<OperationCanceledException>(caughtB, "未開始の B も OCE で決着する");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);

			Assert.AreEqual(2, starts.Count, "Start は開始した A と C の 2 回だけ");
			Assert.AreEqual(2, ends.Count, "End も対で 2 回だけ(未開始の B の分は出ない)");
			Assert.AreSame(idA, starts[0].To);
			Assert.AreSame(idC, starts[1].To);
			Assert.IsFalse(ends[0].Succeeded, "開始済みでキャンセルされた A は Succeeded=false の End が出る");
			Assert.IsTrue(ends[1].Succeeded);
			Assert.IsFalse(starts.Exists(ev => ReferenceEquals(ev.To, idB)), "未開始の B のイベントは一切出ない");
		}
	}
}
