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
	/// フォールトインジェクションテスト第 3 弾。第 1・2 弾(<see cref="FaultInjectionTests"/>)が
	/// 未カバーの境界に故意の失敗を仕込む。注入点ごとの整理:
	/// <list type="bullet">
	/// <item><description>遷移 API に渡すユーザーコールバックの失敗(Push/Pop の Configure、PopTo の predicate)</description></item>
	/// <item><description>補償(クリーンアップ)処理それ自体の失敗(二重フォールト。元の例外がすり替わらない・補償が打ち切られない)</description></item>
	/// <item><description>Pop の復元ロード(完走必須ゾーン)への外部キャンセル(無視されて完走する)</description></item>
	/// <item><description>FIFO キュー待機中の遷移への外部キャンセル(待機側のみ OCE・副作用ゼロ)</description></item>
	/// <item><description>PushAndAwait の結果配送と commit ゾーンフォールトの独立性(退場 hook が落ちても結果は届く)</description></item>
	/// <item><description>レイヤー間のフォールト隔離(Page のハング/失敗が Dialog を妨げない)</description></item>
	/// <item><description>Redirect(hook 内リダイレクト)と発行元/リダイレクト先のフォールトの組み合わせ</description></item>
	/// <item><description>契約違反するユーザー実装(null view を返す handle / 初期化済みでの再 Initialize)</description></item>
	/// <item><description>失敗した遷移のイベント通知(OnTransitionEnd の Succeeded=false)</description></item>
	/// </list>
	/// 不変条件は実装ではなく docs/api-reference.md と各型の XML doc から導いた。
	/// 仕様の根拠と注入点の対応表は docs/fault-injection-3.md にまとめてある。
	/// commit ゾーンの例外は Debug.LogException されるので各テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionTests3
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
		// ユーザーコールバックのフォールト(Configure / predicate)
		// 遷移開始直後・ロード前のユーザーコード境界。何の副作用も出る前なので
		// 「伝播 + 完全無傷 + 復帰可能」が契約(I1/I3)。
		// ===========================================================================

		[Test]
		public async Task Push_ConfigureThrows_Propagates_NoSideEffects_AndRecovers()
		{
			SetupNavigator();
			var factoryInvoked = false;
			var id = new ControllableScreenId(new InstantHandle(), () =>
			{
				factoryInvoked = true;
				return new NullPresenter();
			});

			Exception caught = null;
			try
			{
				await ScreenNavigator.Page.Push(id, new PushOptions
				{
					Configure = _ => throw new InvalidOperationException("fault injected at Push Configure"),
				});
			}
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Configure の失敗は伝播する");
			Assert.IsFalse(factoryInvoked, "Configure はロード前なので presenter 生成にすら到達しない");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task Pop_ConfigureThrows_Propagates_AndStackIsIntact()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			var presenterB = new RecordingPresenter();
			var idB = new ControllableScreenId(new InstantHandle(), () => presenterB);
			await ScreenNavigator.Page.Push(idB);

			Exception caught = null;
			try
			{
				await ScreenNavigator.Page.Pop(new PopOptions
				{
					Configure = _ => throw new InvalidOperationException("fault injected at Pop Configure"),
				});
			}
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "Pop Configure の失敗は伝播する");
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "退場前の失敗なのでスタックは無傷");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
			CollectionAssert.DoesNotContain(presenterB.Events, "BeforeExit", "Exit hook には到達していない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			await ScreenNavigator.Page.Pop();
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "フォールト後も通常の Pop が成立する");
		}

		[Test]
		public async Task PopTo_PredicateThrows_Propagates_AndStackIsIntact()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));
			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);

			Exception caught = null;
			try
			{
				await ScreenNavigator.Page.PopTo(_ => throw new InvalidOperationException("fault injected at PopTo predicate"));
			}
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "predicate の失敗は伝播する");
			Assert.AreEqual(3, ScreenNavigator.Page.History.Count, "対象検索中の失敗なのでスタックは無傷");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			await ScreenNavigator.Page.PopTo(id => id is MarkerScreenId m && m.Label == "A");
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "フォールト後も通常の PopTo が成立する");
		}

		// ===========================================================================
		// 補償処理自体のフォールト(二重フォールト)
		// 第 1・2 弾は「補償が呼ばれること」を確認した。こちらは「補償が失敗したとき」:
		// 元の失敗を隠さない(例外型がすり替わらない)・後続の補償ステップを打ち切らない(I5)。
		// ===========================================================================

		[Test]
		public async Task Push_LoadFails_AndCompensationAlsoFails_OriginalExceptionPropagates()
		{
			SetupNavigator();
			var handle = new FaultyLoadAndUnloadHandle();
			var presenter = new FaultyAfterUnloadPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "補償の失敗で元の例外がすり替わらない");
			StringAssert.Contains("handle.Load", caught.Message, "伝播するのはロードの失敗であって補償の失敗ではない");
			Assert.IsTrue(handle.UnloadCalled, "補償 Unload は試行される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "Unload の失敗後も OnAfterUnload の補償まで進む");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "二重フォールト後も次の Push が成立する");
		}

		[Test]
		public async Task Push_CanceledEntryDiscardUnloadFails_OceStillPropagates_AndCompensationContinues()
		{
			// ct を観測しない hook が完走 → ロード済み entry の破棄(discard)経路で Unload が落ちるケース。
			// 破棄経路の例外はログに留まり、OnAfterUnload までの補償は続き、OCE が伝播する。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at handle\\.Unload"));
			using var cts = new CancellationTokenSource();
			var handle = new InstantLoadFaultyUnloadHandle();
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

		// ===========================================================================
		// 完走必須ゾーンの復元ロードへの外部キャンセル
		// (第 1 弾は退場 hook 中のキャンセル無視のみ。復元ロードの await 境界は別)
		// ===========================================================================

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

		// ===========================================================================
		// キュー待機中の遷移への外部キャンセル
		// (第 1・2 弾の Queue 系は「先行の失敗の隔離」のみ。待機側自身のキャンセルは別)
		// ===========================================================================

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

		// ===========================================================================
		// PushAndAwait の結果配送と commit ゾーンフォールトの独立性
		// (第 1・2 弾は「破棄経路で OCE」のみ。「hook が落ちても結果は届く」側を埋める)
		// ===========================================================================

		[Test]
		public async Task PushAndAwait_ExitHookThrows_ResultIsStillDelivered()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterExit \\(dialog\\)"));
			await ScreenNavigator.Page.Push(new MarkerScreenId("Base"));

			var resultTask = ScreenNavigator.Page.PushAndAwait(new FaultyExitEchoDialogId());
			await ScreenNavigator.Page.Pop();   // 正常 Pop。結果書き込み後の退場 hook が落ちる

			var result = await resultTask;
			Assert.IsNotNull(result, "退場 hook の失敗で結果配送が壊れない");
			Assert.AreEqual("delivered", result.Text);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
		}

		// ===========================================================================
		// レイヤー間のフォールト隔離
		// ===========================================================================

		[Test]
		public async Task PageLayerFault_DoesNotAffectDialogLayer()
		{
			SetupNavigator();
			var source = new UniTaskCompletionSource<IScreenViewInstance>();
			var pagePush = ScreenNavigator.Page.Push(new ControllableScreenId(new ControllableHandle(source)));

			// Page がロード中(遷移中)でも Dialog レイヤーは独立して操作できる
			var idDialog = new MarkerScreenId("D");
			await ScreenNavigator.Dialog.Push(idDialog);
			Assert.AreSame(idDialog, ScreenNavigator.Dialog.Current, "Page の遷移中でも Dialog の Push が成立する");
			Assert.IsTrue(ScreenNavigator.Page.IsTransitioning, "Page 側はまだ遷移中のまま");

			// Page 側をフォールトさせても Dialog 側のスタックは無傷
			source.TrySetException(new InvalidOperationException("fault injected at page load"));
			Exception caught = null;
			try { await pagePush; }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught);
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.AreEqual(1, ScreenNavigator.Dialog.History.Count, "Page のフォールトは Dialog に波及しない");
			Assert.AreSame(idDialog, ScreenNavigator.Dialog.Current);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールトした Page 側も復帰可能");
		}

		// ===========================================================================
		// Redirect(hook 内リダイレクト)とフォールトの組み合わせ
		// ===========================================================================

		[Test]
		public async Task Redirect_IssuedFromThrowingHook_StillExecutes()
		{
			// commit ゾーンの hook が Redirect を発行した直後に throw しても、
			// 現在の遷移は完走し(吸収)、リダイレクトはその後に実行される。
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterEnter \\(redirect origin\\)"));
			var idNext = new MarkerScreenId("Next");
			var id = new ControllableScreenId(new InstantHandle(), () => new RedirectThenThrowPresenter(idNext));

			await ScreenNavigator.Page.Push(id);

			await UniTask.WaitUntil(() => ReferenceEquals(ScreenNavigator.Page.Current, idNext))
				.Timeout(TimeSpan.FromSeconds(5));
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "発行元 hook の失敗でリダイレクトが失われない");
		}

		[Test]
		public async Task Redirect_TargetLoadFails_NavigatorStaysUsable()
		{
			// リダイレクトは fire-and-forget なので失敗は呼び出し元に返らない。
			// 契約は「Navigator が壊れない + OnTransitionEnd(Succeeded=false) で観測できる」まで。
			// Forget 経由の未観測例外ログは経路依存なので一括で無視する。
			SetupNavigator();
			var idTarget = new ControllableScreenId(new FaultyLoadHandle3());
			var id = new ControllableScreenId(new InstantHandle(), () => new RedirectingPresenter(idTarget));
			var failedEnds = 0;
			Action<ScreenTransitionEvent> onEnd = e => { if (!e.Succeeded) Interlocked.Increment(ref failedEnds); };
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			LogAssert.ignoreFailingMessages = true;
			try
			{
				await ScreenNavigator.Page.Push(id);

				await UniTask.WaitUntil(() => failedEnds > 0).Timeout(TimeSpan.FromSeconds(5));

				Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "失敗したリダイレクトは積まれない");
				Assert.AreSame(id, ScreenNavigator.Page.Current, "発行元の画面はそのまま");
				Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

				var idB = new MarkerScreenId("B");
				await ScreenNavigator.Page.Push(idB);
				Assert.AreSame(idB, ScreenNavigator.Page.Current, "リダイレクト失敗後も次の Push が成立する");
			}
			finally
			{
				LogAssert.ignoreFailingMessages = false;
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		// ===========================================================================
		// 契約違反するユーザー実装
		// ===========================================================================

		[Test]
		public async Task HandleLoadReturnsNullView_PushFails_Compensates_AndRecovers()
		{
			// handle が「成功」しつつ null view を返す契約違反。ロールバック可能ゾーンの
			// 失敗として伝播し、補償が走り、Navigator は壊れない。
			SetupNavigator();
			var handle = new NullViewHandle();
			var presenter = new TrackingPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsNotNull(caught, "null view は失敗として伝播する");
			Assert.IsNotInstanceOf<OperationCanceledException>(caught, "キャンセル扱いに化けない");
			Assert.IsTrue(handle.UnloadCalled, "契約違反の handle にも補償 Unload が呼ばれる");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "破棄経路でも OnAfterUnload が呼ばれる");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "契約違反の後も次の Push が成立する");
		}

		[Test]
		public async Task InitializeTwice_Throws_AndExistingNavigatorsRemainUsable()
		{
			SetupNavigator();
			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);

			var extraPage = NewContainer("ExtraPageRoot");
			var extraDialog = NewContainer("ExtraDialogRoot");
			var extraSys = NewContainer("ExtraSysRoot");
			try
			{
				Assert.Throws<InvalidOperationException>(() =>
					ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
					{
						Page = NewLayer(extraPage),
						Dialog = NewLayer(extraDialog),
						SystemDialog = NewLayer(extraSys),
					}), "初期化済みのままの再 Initialize は例外");
			}
			finally
			{
				DestroyContainer(extraPage);
				DestroyContainer(extraDialog);
				DestroyContainer(extraSys);
			}

			Assert.AreSame(idA, ScreenNavigator.Page.Current, "失敗した再 Initialize は既存 Navigator を壊さない");
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "既存 Navigator は引き続き操作できる");
		}

		// ===========================================================================
		// 失敗した遷移のイベント通知
		// ===========================================================================

		[Test]
		public async Task FailedPush_StillFiresTransitionEnd_WithSucceededFalse()
		{
			SetupNavigator();
			var ends = new List<ScreenTransitionEvent>();
			Action<ScreenTransitionEvent> onEnd = ends.Add;
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			try
			{
				Exception caught = null;
				try { await ScreenNavigator.Page.Push(new ControllableScreenId(new FaultyLoadHandle3())); }
				catch (Exception e) { caught = e; }

				Assert.IsInstanceOf<InvalidOperationException>(caught);
				Assert.AreEqual(1, ends.Count, "失敗した遷移でも OnTransitionEnd は発火する");
				Assert.IsFalse(ends[0].Succeeded, "ロールバックされた遷移は Succeeded=false で通知される");
				Assert.AreEqual(ScreenTransitionKind.Push, ends[0].Kind);
			}
			finally
			{
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		// ===========================================================================
		// フォールト注入用のテストダブル
		// ===========================================================================

		/// <summary>全 hook の呼出を記録するだけの presenter(到達しなかったことの検証用)。</summary>
		sealed class RecordingPresenter : IScreenPresenter
		{
			public List<string> Events { get; } = new();

			UniTask Step(string name)
			{
				Events.Add(name);
				return UniTask.CompletedTask;
			}

			UniTask IScreenPresenter.OnInitialize(CancellationToken c) => Step("Initialize");
			UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeLoad");
			UniTask IScreenPresenter.OnAfterLoad(IScreenViewInstance v, INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterLoad");
			UniTask IScreenPresenter.OnBeforeEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("BeforeEnter");
			UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c) => Step("AfterEnter");
			UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("BeforeExit");
			UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c) => Step("AfterExit");
			UniTask IScreenPresenter.OnSuspend(CancellationToken c) => Step("Suspend");
			UniTask IScreenPresenter.OnResume(CancellationToken c) => Step("Resume");
			UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c) => Step("AfterUnload");
		}

		/// <summary>Load が失敗する handle(第 3 弾ローカル。第 1・2 弾の同名 double は private のため再定義)。</summary>
		sealed class FaultyLoadHandle3 : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> UniTask.FromException<IScreenViewInstance>(new InvalidOperationException("fault injected at handle.Load (async)"));
			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		/// <summary>Load も補償の Unload も失敗する handle(二重フォールト用)。</summary>
		sealed class FaultyLoadAndUnloadHandle : IScreenHandle
		{
			public bool UnloadCalled { get; private set; }
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> UniTask.FromException<IScreenViewInstance>(new InvalidOperationException("fault injected at handle.Load (async)"));
			public UniTask Unload(CancellationToken c)
			{
				UnloadCalled = true;
				throw new ApplicationException("fault injected at compensating Unload");
			}
		}

		/// <summary>Load は即成功し、Unload が失敗する handle(破棄経路の二重フォールト用)。</summary>
		sealed class InstantLoadFaultyUnloadHandle : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(new NopView());
			public UniTask Unload(CancellationToken c)
				=> throw new InvalidOperationException("fault injected at handle.Unload");
		}

		/// <summary>OnAfterUnload(補償 hook)が失敗する presenter。呼出有無も記録する。</summary>
		sealed class FaultyAfterUnloadPresenter : IScreenPresenter
		{
			public bool OnAfterUnloadCalled { get; private set; }

			UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
			{
				OnAfterUnloadCalled = true;
				throw new InvalidOperationException("fault injected at compensating AfterUnload");
			}
		}

		/// <summary>OnBeforeLoad で外部 cts を Cancel しつつ自分は正常完了する(ct を観測しない)presenter。</summary>
		sealed class CancelOnBeforeLoadPresenter : IScreenPresenter
		{
			readonly CancellationTokenSource _cts;
			public bool OnAfterUnloadCalled { get; private set; }
			public CancelOnBeforeLoadPresenter(CancellationTokenSource cts) => _cts = cts;

			UniTask IScreenPresenter.OnBeforeLoad(INavigationDataReader r, ITransitionContext x, CancellationToken c)
			{
				_cts.Cancel();
				return UniTask.CompletedTask;
			}

			UniTask IScreenPresenter.OnAfterUnload(INavigationDataWriter w, CancellationToken c)
			{
				OnAfterUnloadCalled = true;
				return UniTask.CompletedTask;
			}
		}

		/// <summary>
		/// 1 回目の Load は即成功し、2 回目(Pop の復元ロード)だけ外部から完了を制御できる handle。
		/// 完走必須ゾーンの復元ロード中にキャンセルをぶつけるために使う。
		/// </summary>
		sealed class SecondLoadControllableHandle : IScreenHandle
		{
			readonly UniTaskCompletionSource<IScreenViewInstance> _secondLoad = new();
			readonly UniTaskCompletionSource _secondLoadStarted = new();
			int _loadCount;
			public UniTask SecondLoadStarted => _secondLoadStarted.Task;
			public void CompleteSecondLoad() => _secondLoad.TrySetResult(new NopView());

			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
			{
				if (++_loadCount == 1) return UniTask.FromResult<IScreenViewInstance>(new NopView());
				_secondLoadStarted.TrySetResult();
				return _secondLoad.Task;
			}

			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		/// <summary>OnBeforeExit で結果を書き込み、直後の OnAfterExit で throw する結果ダイアログ用 presenter。</summary>
		sealed class ResultThenThrowDialogPresenter : IScreenPresenter
		{
			UniTask IScreenPresenter.OnBeforeExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
			{
				w.Write(new EchoResult { Text = "delivered" });
				return UniTask.CompletedTask;
			}

			UniTask IScreenPresenter.OnAfterExit(INavigationDataWriter w, ITransitionContext x, CancellationToken c)
				=> throw new InvalidOperationException("fault injected at AfterExit (dialog)");
		}

		/// <summary>結果書き込み後に退場 hook が落ちる、結果を返すダイアログの identifier。</summary>
		sealed record FaultyExitEchoDialogId : ScreenIdentifier<EchoResult>
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new ResultThenThrowDialogPresenter();
		}

		/// <summary>OnAfterEnter で Redirect を発行した直後に throw する presenter。</summary>
		sealed class RedirectThenThrowPresenter : IScreenPresenter
		{
			readonly IScreenIdentifier _next;
			public RedirectThenThrowPresenter(IScreenIdentifier next) => _next = next;

			UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c)
			{
				ScreenNavigator.Page.Push(_next, new PushOptions { InterruptPriority = InterruptPriority.Queue }).Redirect();
				throw new InvalidOperationException("fault injected at AfterEnter (redirect origin)");
			}
		}

		/// <summary>OnAfterEnter で指定先へ Redirect を発行する presenter(自分は正常完了する)。</summary>
		sealed class RedirectingPresenter : IScreenPresenter
		{
			readonly IScreenIdentifier _next;
			public RedirectingPresenter(IScreenIdentifier next) => _next = next;

			UniTask IScreenPresenter.OnAfterEnter(INavigationDataReader r, ITransitionContext x, CancellationToken c)
			{
				ScreenNavigator.Page.Push(_next, new PushOptions { InterruptPriority = InterruptPriority.Queue }).Redirect();
				return UniTask.CompletedTask;
			}
		}

		/// <summary>Load が「成功」しつつ null view を返す契約違反 handle。</summary>
		sealed class NullViewHandle : IScreenHandle
		{
			public bool UnloadCalled { get; private set; }
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(null);
			public UniTask Unload(CancellationToken c) { UnloadCalled = true; return UniTask.CompletedTask; }
		}
	}
}
