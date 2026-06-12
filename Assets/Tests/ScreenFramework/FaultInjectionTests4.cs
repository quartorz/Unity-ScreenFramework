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
	/// フォールトインジェクションテスト第 4 弾。第 1〜3 弾(<see cref="FaultInjectionTests"/> /
	/// <see cref="FaultInjectionTests3"/>)が未カバーの組み合わせ境界に故意の失敗を仕込む。
	/// 注入点ごとの整理:
	/// <list type="bullet">
	/// <item><description>キャンセルと複合操作の組み合わせ(Change のロード中キャンセル、DismissAll へのキャンセル無視、WaitForStage の外部キャンセル)</description></item>
	/// <item><description>割り込みと複合操作の組み合わせ(Replace が preempt される、DismissAll が進行中ロードを preempt する)</description></item>
	/// <item><description>中間 Close(最上段以外を参照で閉じる経路)の teardown フォールトと、死んだ entry への Close の無害性</description></item>
	/// <item><description>Close 経由の結果配送と OnAfterUnload の「最後の書き込みチャンス」(退場 hook フォールト下)</description></item>
	/// <item><description>遷移中に積まれた History.Edit がその遷移の失敗後も適用されること</description></item>
	/// <item><description>失敗した rollback hook から発行された Redirect の生存</description></item>
	/// <item><description>Shutdown と進行中遷移・pending awaiter・即時再 Initialize の組み合わせ</description></item>
	/// <item><description>Initialize の部分検証失敗(原子性)</description></item>
	/// </list>
	/// 不変条件は実装ではなく docs/api-reference.md と各型の XML doc から導いた。
	/// 仕様の根拠と注入点の対応表は docs/fault-injection-4.md にまとめてある。
	/// commit ゾーンの例外は Debug.LogException されるので各テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionTests4
	{
		IScreenContainer _pageContainer;

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード(既初期化なら throw)があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_pageContainer);
		}

		void SetupNavigator(ScreenCacheMode cache = ScreenCacheMode.DestroyOnCover)
		{
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer, cache: cache),
				Dialog = NewLayer(NewContainer("DialogRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			});
		}

		// ===========================================================================
		// キャンセル × 複合操作
		// 第 1 弾のキャンセル系は Push/Pop/PushAndAwait のみ。ゾーン区分は操作横断の
		// 設計原則なので、Change(rollback)と DismissAll(全段完走必須)でも成立すること
		// を見る(I11)。
		// ===========================================================================

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
			var gated = new GatedExitPresenter4();
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

		[Test]
		public async Task Push_WaitForStageCanceledExternally_RollsBackWithOce()
		{
			// 第 2 弾は「publish されない stage への待ちは timeout で決着」。こちらはそのキャンセル形:
			// timeout なしの WaitForStage が外部キャンセルで OCE 決着し、遷移は補償付きで巻き戻る。
			SetupNavigator();
			using var cts = new CancellationTokenSource();
			var handle = new InstantHandle();
			var presenter = new StageWaitCancelPresenter4();
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

		// ===========================================================================
		// キャンセルされた遷移のイベント通知
		// (第 3 弾は失敗遷移の Succeeded=false のみ。キャンセル形を埋める)
		// ===========================================================================

		[Test]
		public async Task CanceledPush_FiresTransitionEnd_WithSucceededFalse()
		{
			SetupNavigator();
			var ends = new List<ScreenTransitionEvent>();
			Action<ScreenTransitionEvent> onEnd = ends.Add;
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			try
			{
				using var cts = new CancellationTokenSource();
				var source = new UniTaskCompletionSource<IScreenViewInstance>();
				var pushTask = ScreenNavigator.Page.Push(new ControllableScreenId(new ControllableHandle(source)), ct: cts.Token);
				cts.Cancel();

				Exception caught = null;
				try { await pushTask; }
				catch (Exception e) { caught = e; }

				Assert.IsInstanceOf<OperationCanceledException>(caught);
				Assert.AreEqual(1, ends.Count, "キャンセルされた遷移でも OnTransitionEnd は発火する");
				Assert.IsFalse(ends[0].Succeeded, "キャンセルされた遷移は Succeeded=false で通知される");
				Assert.AreEqual(ScreenTransitionKind.Push, ends[0].Kind);
			}
			finally
			{
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		// ===========================================================================
		// 割り込み × 複合操作
		// (第 1・2 弾の Preempt 系は Push/Pop/PushAndAwait のみ)
		// ===========================================================================

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

		// ===========================================================================
		// 中間 Close の teardown フォールトと、死んだ entry への Close
		// (第 1〜3 弾の Close 系は最上段 / 最後の 1 枚のみ。中間経路と no-op 契約を埋める)
		// ===========================================================================

		[Test]
		public async Task CloseMiddle_UnloadThrows_CloseCompletes_AndStackStaysCoherent()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover);   // 中間 A を生きたまま(suspended)Close に巻き込む
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at handle\\.Unload"));

			var presenterA = new TrackingPresenter();
			var entryA = await ScreenNavigator.Page.Push(new ControllableScreenId(new FaultyUnloadHandle4(), () => presenterA));
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);

			await entryA.Close();   // 最上段ではない = 中間 Close 経路

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "teardown の失敗で中間 Close が中断しない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "最上段は据え置かれる");
			Assert.IsFalse(entryA.IsAlive, "Unload が失敗した entry も閉じ切られる");
			Assert.IsTrue(presenterA.OnAfterUnloadCalled, "Unload の失敗後も OnAfterUnload まで進む");

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task EntryClose_AfterSweptByDismissAll_IsNoOp()
		{
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var entry = await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			await ScreenNavigator.Page.DismissAll();
			Assert.IsFalse(entry.IsAlive, "DismissAll で破棄された entry は IsAlive=false");

			await entry.Close();   // 「既に閉じている / 未 Push なら何もしない」契約。例外にならない

			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "死んだ entry への Close は状態を変えない");

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "no-op の後も次の Push が成立する");
		}

		// ===========================================================================
		// Close 経由の結果配送 + OnAfterUnload の「最後の書き込みチャンス」
		// (第 3 弾は「正常 Pop + OnBeforeExit で書き込み + OnAfterExit フォールト」。
		// こちらは Close 経路 + 最終 hook での書き込み + より早い hook のフォールト)
		// ===========================================================================

		[Test]
		public async Task PushAndAwait_ClosedViaEntry_BeforeExitThrows_LastChanceWriteIsDelivered()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit \\(last-chance dialog\\)"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));

			var resultTask = ScreenNavigator.Page.PushAndAwait(new LastChanceEchoDialogId());
			var entry = ScreenNavigator.Page.FindEntry<LastChanceEchoPresenter4>();
			Assert.IsNotNull(entry, "開いたダイアログのエントリが見つかる前提");

			await entry.Close();   // Close は「参照で閉じる Pop」= 正常クローズ扱い

			var result = await resultTask;
			Assert.IsNotNull(result, "退場 hook の失敗で結果配送が壊れない");
			Assert.AreEqual("last-chance", result.Text, "OnAfterUnload の書き込みが結果配送に間に合う");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
		}

		// ===========================================================================
		// 遅延 History.Edit × 失敗した遷移
		// (第 2 弾は「遅延 Edit 自体の throw」と「Edit で外した entry の Unload throw」。
		// こちらは「遅延の原因になった遷移が失敗しても Edit は失われない」)
		// ===========================================================================

		[Test]
		public async Task HistoryEdit_DeferredDuringFailedTransition_IsStillApplied()
		{
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);

			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var pushX = ScreenNavigator.Page.Push(new ControllableScreenId(new ControllableHandle(source)));
			Assert.IsTrue(ScreenNavigator.Page.IsTransitioning, "この時点で遷移中である前提");

			ScreenNavigator.Page.History.Edit(e => e.RemoveAt(0));   // 遷移中なので遅延される
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "遷移中の Edit は即時適用されない");

			source.TrySetException(new InvalidOperationException("fault injected at deferred-edit load"));
			Exception caught = null;
			try { await pushX; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "遷移が失敗しても遅延された Edit は適用される");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current, "編集適用後も次の Push が成立する");
		}

		// ===========================================================================
		// Redirect × 失敗した rollback 遷移
		// (第 3 弾は commit ゾーン発行元の throw と、リダイレクト先の失敗。
		// こちらは「発行元の遷移そのものがロールバックされる」ケース)
		// ===========================================================================

		[Test]
		public async Task Redirect_IssuedFromFailedRollbackHook_StillExecutes()
		{
			// rollback ゾーンの hook が Redirect(Queue)を発行した直後に throw しても、
			// 発行元の遷移は補償付きで巻き戻り(失敗は呼び出し側へ伝播)、
			// リダイレクトは FIFO の契約どおり先行の失敗を引き継がず実行される。
			SetupNavigator();
			var idNext = new MarkerScreenId("Next");
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => new RedirectThenFailBeforeLoadPresenter4(idNext));

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "発行元の失敗は呼び出し側へ伝播する");
			Assert.IsTrue(handle.UnloadCalled, "発行元のロード済み view は補償 Unload される");

			await UniTask.WaitUntil(() => ReferenceEquals(ScreenNavigator.Page.Current, idNext))
				.Timeout(TimeSpan.FromSeconds(5));
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "発行元のロールバックでリダイレクトが失われない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		// ===========================================================================
		// Shutdown × 進行中遷移 / pending awaiter / 即時再 Initialize
		// (第 2 弾は「Shutdown 中の hook フォールト」のみ)
		// ===========================================================================

		[Test]
		public async Task Shutdown_DuringCommitZonePush_PushCompletes_ThenEverythingIsCleared()
		{
			SetupNavigator();
			var nav = ScreenNavigator.Page;
			var gated = new GatedPresenter();
			var id = new ControllableScreenId(new InstantHandle(), () => gated);

			var pushTask = ScreenNavigator.Page.Push(id);
			await gated.Started;   // OnBeforeEnter = 完走必須ゾーンで停止中

			var shutdownTask = ScreenNavigator.Shutdown();
			Assert.IsNull(ScreenNavigator.Page, "静的参照は Shutdown 呼び出しと同期に外れる");

			gated.Release();
			var entry = await pushTask;   // 完走必須ゾーンの遷移は Shutdown に殺されず完走する
			Assert.IsNotNull(entry, "進行中だった Push は正常に決着する");

			await shutdownTask;
			Assert.AreEqual(0, nav.History.Count, "完走を待ってから全画面が畳まれる");
			Assert.IsFalse(nav.IsTransitioning);

			// 畳み終わった後は通常どおり再 Initialize できる
			DestroyContainer(_pageContainer);
			SetupNavigator();
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "Shutdown 後の再 Initialize で通常運転に戻れる");
		}

		[Test]
		public async Task Shutdown_PendingPushAndAwait_AwaiterSettlesWithOce()
		{
			SetupNavigator();
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));
			var resultTask = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("never"));

			await ScreenNavigator.Shutdown();

			Exception caught = null;
			try { await resultTask; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<OperationCanceledException>(caught, "Shutdown に畳まれた awaiter は OCE で決着する(ハングしない)");
			Assert.IsNull(ScreenNavigator.Page, "静的参照は畳まれている");
		}

		[Test]
		public async Task Reinitialize_BeforeShutdownCompletes_IsUnaffectedByOldTeardownFault()
		{
			// Shutdown は静的参照を同期的に外すので、退場演出が終わる前でも再 Initialize できる契約。
			// 旧レイヤーの teardown フォールト(OnAfterUnload throw)が新 Navigator に波及しないことを見る。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterUnload \\(gated\\)"));
			var oldNav = ScreenNavigator.Page;
			var oldContainer = _pageContainer;
			await ScreenNavigator.Page.Push(new MarkerScreenId("Old"));
			var gated = new GatedExitPresenter4(failAfterUnload: true);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => gated));

			var shutdownTask = ScreenNavigator.Shutdown();
			await gated.Started;   // 旧レイヤーは退場 hook で停止中(畳み切っていない)

			SetupNavigator();   // 静的参照は外れているので即 Initialize できる
			var idNew = new MarkerScreenId("New");
			await ScreenNavigator.Page.Push(idNew);
			Assert.AreSame(idNew, ScreenNavigator.Page.Current, "旧レイヤーの畳み中でも新 Navigator は通常運転できる");

			gated.Release();
			await shutdownTask;

			Assert.AreEqual(0, oldNav.History.Count, "teardown フォールトがあっても旧レイヤーは畳み切られる");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "旧レイヤーのフォールトは新 Navigator に波及しない");
			Assert.AreSame(idNew, ScreenNavigator.Page.Current);

			DestroyContainer(oldContainer);
		}

		// ===========================================================================
		// Initialize の部分検証失敗(原子性)
		// Initialize は全レイヤーをローカルに組み立ててから一括代入するので、途中の検証 throw でも
		// static 参照に部分状態を残さない。docs/fault-injection-4.md §3 を参照。
		// ===========================================================================

		[Test]
		public async Task Initialize_PartialValidationFailure_LeavesNothingInitialized()
		{
			// Dialog レイヤーの Container 欠落で Initialize が途中失敗するケース。
			// 失敗した Initialize が「初期化済み」状態を残すと、成功していないのに
			// Shutdown を要求される自己矛盾になるため、全静的参照は null のままであるべき。
			var pageC = NewContainer("AtomicPageRoot");
			var sysC = NewContainer("AtomicSysRoot");
			try
			{
				Assert.Throws<ArgumentException>(() =>
					ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
					{
						Page = NewLayer(pageC),
						Dialog = new ScreenLayerConfig { Container = null },   // 検証で throw する
						SystemDialog = NewLayer(sysC),
					}), "Container 欠落は Initialize の時点で fail-fast する");

				Assert.IsNull(ScreenNavigator.Page, "失敗した Initialize は Page を初期化済みにしない");
				Assert.IsNull(ScreenNavigator.Dialog, "失敗した Initialize は Dialog を初期化済みにしない");
				Assert.IsNull(ScreenNavigator.SystemDialog, "失敗した Initialize は SystemDialog を初期化済みにしない");

				// 部分状態が残っていなければ、Shutdown を挟まず正しい設定での Initialize がそのまま成立する。
				SetupNavigator();
				var idA = new MarkerScreenId("A");
				await ScreenNavigator.Page.Push(idA);
				Assert.AreSame(idA, ScreenNavigator.Page.Current, "失敗した Initialize の後も正しい Initialize で復帰できる");
			}
			finally
			{
				DestroyContainer(pageC);
				DestroyContainer(sysC);
			}
		}

		// ===========================================================================
		// フォールト注入用のテストダブル
		// ===========================================================================

		/// <summary>Load は即成功し、Unload が失敗する handle(第 4 弾ローカル。第 1〜3 弾の同名 double は private のため再定義)。</summary>
		sealed class FaultyUnloadHandle4 : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(new NopView());
			public UniTask Unload(CancellationToken c)
				=> throw new InvalidOperationException("fault injected at handle.Unload");
		}

		/// <summary>
		/// OnBeforeExit で Started を立ててから Release まで待機する presenter。
		/// オプションで OnAfterUnload を throw させ、teardown フォールトを重ねられる。
		/// </summary>
		sealed class GatedExitPresenter4 : IScreenPresenter
		{
			readonly bool _failAfterUnload;
			readonly UniTaskCompletionSource _started = new();
			readonly UniTaskCompletionSource _release = new();
			public UniTask Started => _started.Task;
			public void Release() => _release.TrySetResult();
			public GatedExitPresenter4(bool failAfterUnload = false) => _failAfterUnload = failAfterUnload;

			UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
			{
				_started.TrySetResult();
				return _release.Task;
			}

			UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
				=> _failAfterUnload
					? throw new InvalidOperationException("fault injected at AfterUnload (gated)")
					: UniTask.CompletedTask;
		}

		/// <summary>誰も publish しない stage key(WaitForStage のキャンセル決着テスト用)。</summary>
		sealed class NeverPublishedStage4 : IStageKey { }

		/// <summary>OnBeforeLoad で NeverPublishedStage4 を timeout なし・ct 付きで待つ presenter。OnAfterUnload の呼出も記録する。</summary>
		sealed class StageWaitCancelPresenter4 : IScreenPresenter
		{
			public bool OnAfterUnloadCalled { get; private set; }

			UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c)
				=> x.WaitForStage<NeverPublishedStage4>(c);

			UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
			{
				OnAfterUnloadCalled = true;
				return UniTask.CompletedTask;
			}
		}

		/// <summary>OnBeforeExit で throw し、OnAfterUnload(最後の書き込みチャンス)で結果を書く presenter。</summary>
		sealed class LastChanceEchoPresenter4 : IScreenPresenter
		{
			UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
				=> throw new InvalidOperationException("fault injected at BeforeExit (last-chance dialog)");

			UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
			{
				w.Write(new EchoResult { Text = "last-chance" });
				return UniTask.CompletedTask;
			}
		}

		/// <summary>退場 hook が落ちつつ OnAfterUnload で結果を書く、結果を返すダイアログの identifier。</summary>
		sealed record LastChanceEchoDialogId : ScreenIdentifier<EchoResult>
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new LastChanceEchoPresenter4();
		}

		/// <summary>OnBeforeLoad(rollback ゾーン)で Redirect を発行した直後に throw する presenter。</summary>
		sealed class RedirectThenFailBeforeLoadPresenter4 : IScreenPresenter
		{
			readonly IScreenIdentifier _next;
			public RedirectThenFailBeforeLoadPresenter4(IScreenIdentifier next) => _next = next;

			UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c)
			{
				ScreenNavigator.Page.Push(_next, new PushOptions { InterruptPriority = InterruptPriority.Queue }).Redirect();
				throw new InvalidOperationException("fault injected at BeforeLoad (redirect origin)");
			}
		}
	}
}
