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
	/// フォールトインジェクションテスト。既存テストが触れていない注入点に故意に失敗を仕込み、
	/// 「rollback ゾーンの失敗は伝播 + 完全クリーンアップ」「commit ゾーンの失敗はログに留めて完走」
	/// 「どのフォールト後も Navigator は次の操作を受け付けられる（復帰可能）」の 3 契約を検証する。
	/// 注入点ごとの整理:
	/// - handle.Load 自体の失敗（同期 throw / faulted UniTask / OnBeforeLoad との同時失敗）
	/// - OnInitialize の失敗（handle 生成前なので handle に触らない）
	/// - Pop の復元ロード失敗（commit ゾーンだが非ガード = 伝播する設計）
	/// - commit ゾーンの未カバー hook（OnBeforeExit / handle.Unload / OnAfterUnload / OnSuspend / OnResume）
	/// - View 演出（PlayEnter / PlayExit）の失敗
	/// - 遷移イベント購読者の失敗
	/// - 複合操作（DismissAll / Reset）中の hook 失敗
	/// commit ゾーンの例外は Debug.LogException されるので各テストで <see cref="LogAssert.Expect"/> する。
	/// </summary>
	public sealed class FaultInjectionTests
	{
		IScreenContainer _pageContainer;

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード（既初期化なら throw）があるので、各テスト後に静的参照を畳む。
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
		// handle.Load のフォールト（既存テストは presenter hook の失敗のみで、handle 自体は未カバー）
		// ===========================================================================

		[Test]
		public async Task HandleLoadFaulted_PushPropagates_CleansUp_AndNavigatorRecovers()
		{
			SetupNavigator();
			var handle = new FaultyLoadHandle();
			var presenter = new TrackingPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "rollback ゾーンの load 失敗は伝播する");
			Assert.IsTrue(handle.UnloadCalled, "失敗した load も handle.Unload で補償される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled, "load 失敗時も OnAfterUnload を呼ぶ契約");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "失敗した Push は追跡されない");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning, "失敗後に遷移中フラグが残らない");

			// フォールト後も次の操作が成立する（復帰可能）
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task HandleLoadThrowsSynchronously_PushPropagates_AndCleansUp()
		{
			// faulted UniTask とは別経路（loadTask 起動前の同期 throw）。catch 側の
			// 「未起動タスクの決着待ちを skip して cleanup する」分岐を踏む。
			SetupNavigator();
			var handle = new FaultyLoadHandle(throwSynchronously: true);
			var presenter = new TrackingPresenter();
			var id = new ControllableScreenId(handle, () => presenter);

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught);
			Assert.IsTrue(handle.UnloadCalled, "同期 throw でも handle.Unload で補償される");
			Assert.IsTrue(presenter.OnAfterUnloadCalled);
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
		}

		[Test]
		public async Task HandleLoadAndOnBeforeLoadBothFail_PropagatesNonOce_AndUnloads()
		{
			// 並列起動される handle.Load と presenter.OnBeforeLoad が同時に失敗しても、
			// 互いの決着を待ってから cleanup し、元の例外型のまま（OCE に化けず）伝播する。
			SetupNavigator();
			var handle = new FaultyLoadHandle();
			var id = new ControllableScreenId(handle, () => new ThrowingOnBeforeLoadPresenter());

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsNotNull(caught, "二重失敗でも例外は伝播する");
			Assert.IsNotInstanceOf<OperationCanceledException>(caught, "二重失敗でも OCE に詰め替えられない");
			Assert.IsTrue(handle.UnloadCalled, "二重失敗でも handle.Unload で補償される");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
		}

		// ===========================================================================
		// OnInitialize のフォールト（rollback ゾーンの最初期。handle 生成より前）
		// ===========================================================================

		[Test]
		public async Task OnInitializeThrows_PushPropagates_AndHandleIsNeverTouched()
		{
			SetupNavigator();
			var handle = new InstantHandle();
			var id = new ControllableScreenId(handle, () => new FaultyPresenter("Initialize"));

			Exception caught = null;
			try { await ScreenNavigator.Page.Push(id); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "OnInitialize の例外は伝播する");
			Assert.IsFalse(handle.UnloadCalled, "handle は OnInitialize より後に作られるので補償 Unload も呼ばれない");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);

			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idB);
			Assert.AreSame(idB, ScreenNavigator.Page.Current, "フォールト後も次の Push が成立する");
		}

		// ===========================================================================
		// Pop の復元ロード失敗（commit ゾーンだが CreateAndPreloadAsync は非ガード = 伝播する設計）
		// ===========================================================================

		[Test]
		public async Task Pop_RestoreLoadFails_PropagatesButNavigatorRemainsUsable()
		{
			SetupNavigator(); // DestroyOnCover: 覆われた A は Pop 時に再ロードされる
			var creations = 0;
			// 1 回目（Push 時）は成功し、2 回目（Pop の復元時）だけ失敗する presenter factory
			var idA = new ControllableScreenId(new InstantHandle(), () =>
				++creations == 1 ? new NullPresenter() : (IScreenPresenter)new ThrowingOnBeforeLoadPresenter());
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			Exception caught = null;
			try { await ScreenNavigator.Page.Pop(); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "復元ロードの失敗は呼び出し側へ伝播する");
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "B の退場は完了している（巻き戻さない）");
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "履歴上の Current は A のまま（dormant）");
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			// dormant な最上段の上にも通常の Push が成立する（黒画面からの復帰経路がある）
			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Push(idC);
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
		}

		// ===========================================================================
		// commit ゾーンの未カバー hook（CommitZoneGuardTests は BeforeEnter/AfterEnter/AfterExit のみ）
		// ===========================================================================

		[Test]
		public async Task Pop_OnBeforeExitThrows_PopCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeExit")));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "退場 hook の失敗で Pop が中断しない");
		}

		[Test]
		public async Task Pop_HandleUnloadThrows_PopCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at handle\\.Unload"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new FaultyUnloadHandle()));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "Unload の失敗で bookkeeping が止まらない");
		}

		[Test]
		public async Task Pop_OnAfterUnloadThrows_PopCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterUnload"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("AfterUnload")));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Push_CoveredScreenOnSuspendThrows_PushCompletes_AndResumeStillWorks()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover);
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at Suspend"));

			var presenterA = new FaultyPresenter("Suspend");
			var idA = new ControllableScreenId(new InstantHandle(), () => presenterA);
			var idB = new MarkerScreenId("B");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(idB);

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count, "OnSuspend の失敗で Push が中断しない");
			Assert.AreSame(idB, ScreenNavigator.Page.Current);

			// Suspended フラグは hook の失敗後も立っており、Pop で通常どおり Resume される
			await ScreenNavigator.Page.Pop();
			CollectionAssert.Contains(presenterA.Events, "Resume");
			Assert.AreSame(idA, ScreenNavigator.Page.Current);
		}

		[Test]
		public async Task Pop_OnResumeThrows_PopCompletes()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover);
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at Resume"));

			var idA = new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("Resume"));
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new MarkerScreenId("B"));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "OnResume の失敗で Pop が中断しない");
		}

		// ===========================================================================
		// View 演出（PlayEnter / PlayExit）のフォールト
		// ===========================================================================

		[Test]
		public async Task Push_PlayEnterThrows_PushCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at PlayEnter"));

			var id = new ControllableScreenId(new WrappingHandle(new FaultyAnimView(failEnter: true)));
			await ScreenNavigator.Page.Push(id);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(id, ScreenNavigator.Page.Current, "入場演出の失敗で画面が孤児にならない");
		}

		[Test]
		public async Task Pop_PlayExitThrows_PopCompletes()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at PlayExit"));

			var idA = new MarkerScreenId("A");
			await ScreenNavigator.Page.Push(idA);
			await ScreenNavigator.Page.Push(new ControllableScreenId(new WrappingHandle(new FaultyAnimView(failExit: true))));

			await ScreenNavigator.Page.Pop();

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.AreSame(idA, ScreenNavigator.Page.Current, "退場演出の失敗で Pop が中断しない");
		}

		// ===========================================================================
		// 遷移イベント購読者のフォールト
		// ===========================================================================

		[Test]
		public async Task TransitionEventHandlersThrow_TransitionStillSucceeds()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("observer fault at start"));
			LogAssert.Expect(LogType.Exception, new Regex("observer fault at end"));

			Action<ScreenTransitionEvent> onStart = _ => throw new InvalidOperationException("observer fault at start");
			Action<ScreenTransitionEvent> onEnd = _ => throw new InvalidOperationException("observer fault at end");
			ScreenNavigator.Page.OnTransitionStart += onStart;
			ScreenNavigator.Page.OnTransitionEnd += onEnd;
			try
			{
				var idA = new MarkerScreenId("A");
				await ScreenNavigator.Page.Push(idA);

				Assert.AreSame(idA, ScreenNavigator.Page.Current, "購読者の例外は遷移本筋に影響しない");
				Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			}
			finally
			{
				// TearDown の Shutdown(DismissAll) でも発火するので、テスト末尾で必ず外す
				ScreenNavigator.Page.OnTransitionStart -= onStart;
				ScreenNavigator.Page.OnTransitionEnd -= onEnd;
			}
		}

		// ===========================================================================
		// 複合操作（DismissAll / Reset）中の hook フォールト
		// ===========================================================================

		[Test]
		public async Task DismissAll_WithThrowingExitHooks_StillClearsEverything()
		{
			SetupNavigator(ScreenCacheMode.KeepOnCover); // A も生かしたまま DismissAll に巻き込む
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at BeforeExit"));
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterUnload"));

			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("AfterUnload")));
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("BeforeExit")));

			await ScreenNavigator.Page.DismissAll();

			Assert.AreEqual(0, ScreenNavigator.Page.History.Count, "退場 hook が落ちても全画面が畳まれる");
		}

		[Test]
		public async Task Reset_TopExitHookThrows_StillCollapsesToNewScreen()
		{
			SetupNavigator();
			LogAssert.Expect(LogType.Exception, new Regex("fault injected at AfterExit"));

			await ScreenNavigator.Page.Push(new MarkerScreenId("A"));
			await ScreenNavigator.Page.Push(new ControllableScreenId(new InstantHandle(), () => new FaultyPresenter("AfterExit")));

			var idC = new MarkerScreenId("C");
			await ScreenNavigator.Page.Reset(idC);

			Assert.AreEqual(1, ScreenNavigator.Page.History.Count, "破壊フェーズの hook 失敗で Reset が中断しない");
			Assert.AreSame(idC, ScreenNavigator.Page.Current);
		}

		// ===========================================================================
		// PushAndAwait のフォールト（失敗時にハングしないこと）
		// ===========================================================================

		[Test]
		public async Task PushAndAwait_LoadFaulted_PropagatesInsteadOfHanging()
		{
			SetupNavigator();

			Exception caught = null;
			try { await ScreenNavigator.Page.PushAndAwait(new FaultyLoadDialogId()); }
			catch (Exception e) { caught = e; }

			Assert.IsInstanceOf<InvalidOperationException>(caught, "ロード失敗は結果待ちのハングではなく例外で返る");
			Assert.AreEqual(0, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		}

		// ===========================================================================
		// フォールト注入用のテストダブル
		// ===========================================================================

		/// <summary>Load が失敗する handle。同期 throw と faulted UniTask の両経路を再現できる。</summary>
		sealed class FaultyLoadHandle : IScreenHandle
		{
			readonly bool _throwSynchronously;
			public bool UnloadCalled { get; private set; }
			public FaultyLoadHandle(bool throwSynchronously = false) => _throwSynchronously = throwSynchronously;

			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> _throwSynchronously
					? throw new InvalidOperationException("fault injected at handle.Load (sync)")
					: UniTask.FromException<IScreenViewInstance>(new InvalidOperationException("fault injected at handle.Load (async)"));

			public UniTask Unload(CancellationToken c) { UnloadCalled = true; return UniTask.CompletedTask; }
		}

		/// <summary>Unload が失敗する handle。Load は即座に NopView を返す。</summary>
		sealed class FaultyUnloadHandle : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(new NopView());
			public UniTask Unload(CancellationToken c)
				=> throw new InvalidOperationException("fault injected at handle.Unload");
		}

		/// <summary>指定した hook で例外を投げつつ、全 hook の呼出を記録する presenter。</summary>
		sealed class FaultyPresenter : IScreenPresenter
		{
			readonly string _faultAt;
			public List<string> Events { get; } = new();
			public FaultyPresenter(string faultAt = null) => _faultAt = faultAt;

			UniTask Step(string name)
			{
				Events.Add(name);
				if (name == _faultAt) throw new InvalidOperationException($"fault injected at {name}");
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

		/// <summary>PlayEnter / PlayExit を指定で失敗させる view。</summary>
		sealed class FaultyAnimView : IScreenAnimatedView
		{
			readonly bool _failEnter;
			readonly bool _failExit;
			public FaultyAnimView(bool failEnter = false, bool failExit = false)
			{
				_failEnter = failEnter;
				_failExit = failExit;
			}

			public UniTask PlayEnter(CancellationToken c)
				=> _failEnter ? throw new InvalidOperationException("fault injected at PlayEnter") : UniTask.CompletedTask;
			public UniTask PlayExit(CancellationToken c)
				=> _failExit ? throw new InvalidOperationException("fault injected at PlayExit") : UniTask.CompletedTask;
		}

		/// <summary>任意のオブジェクトを view インスタンスとして返す handle（As&lt;T&gt; で中身が出る）。</summary>
		sealed class WrappingHandle : IScreenHandle
		{
			readonly object _view;
			public WrappingHandle(object view) => _view = view;
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult(ScreenTesting.ViewOf(_view));
			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		/// <summary>Load が必ず失敗する、結果を返すダイアログの identifier（PushAndAwait 用）。</summary>
		sealed record FaultyLoadDialogId : ScreenIdentifier<EchoResult>
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new FaultyLoadHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
		}
	}
}
