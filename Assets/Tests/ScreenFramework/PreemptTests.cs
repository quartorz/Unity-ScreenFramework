using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;
using Tests.Support;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// 進行中の遷移が新しい操作で preempt される挙動を検証する。
	/// </summary>
	public sealed class PreemptTests
	{
		IScreenContainer _pageContainer;

		[SetUp]
		public void SetUp()
		{
			_pageContainer = NewContainer("PageRoot");
			var registry = TestSampleRegistry.AllMocks();
			var setup = new ScreenLayerSetup
			{
				Page = NewLayerConfig(_pageContainer),
				Dialog = NewLayerConfig(NewContainer("DialogRoot")),
				SystemDialog = NewLayerConfig(NewContainer("SystemDialogRoot")),
			};
			ScreenNavigator.Initialize(registry, setup);
		}

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード（既初期化なら throw）があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			DestroyContainer(_pageContainer);
		}

		[UnityTest]
		public IEnumerator Push_WhileLoading_IsPreempted_ByNextPush() => UniTask.ToCoroutine(async () =>
		{
			var slowSource = new UniTaskCompletionSource<IScreenViewInstance>();
			var handleA = new ControllableHandle(slowSource);
			var idA = new ControllableScreenId(handleA);

			// 完了させない Push を開始
			var pushA = ScreenNavigator.Page.Push(idA);

			// 1 フレーム進めて Load 中まで進める
			await UniTask.Yield();

			// 別の Push（モック即時完了）で preempt
			var pushB = ScreenNavigator.Page.Push(new HomeScreenId());

			// A が Cancelled で抜けるはず
			try { await pushA; Assert.Fail("pushA should have been cancelled"); }
			catch (OperationCanceledException) { /* 期待動作 */ }

			// ハンドル A の Unload が呼ばれていてリークしないことを確認
			Assert.IsTrue(handleA.UnloadCalled, "Handle A should have been unloaded on cancellation");

			await pushB;

			// B が現在画面
			Assert.IsInstanceOf<HomeScreenId>(ScreenNavigator.Page.Current);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);

			// 取り残し防止のため A の Load を後から完了させても安全
			slowSource.TrySetResult(new MockScreenViewInstanceProxy());
			await UniTask.Yield();
		});

		[UnityTest]
		public IEnumerator Queue_WaitsForCurrent() => UniTask.ToCoroutine(async () =>
		{
			var slowSource = new UniTaskCompletionSource<IScreenViewInstance>();
			var handleA = new ControllableHandle(slowSource);
			var idA = new ControllableScreenId(handleA);

			var pushA = ScreenNavigator.Page.Push(idA);
			await UniTask.Yield();

			// Queue 指定で次を待たせる
			var pushB = ScreenNavigator.Page.Push(new HomeScreenId(),
				new PushOptions { InterruptPriority = InterruptPriority.Queue });

			// 1 フレーム進めても B は完了しないはず（A 待ち）
			await UniTask.Yield();
			Assert.IsNull(ScreenNavigator.Page.Current, "B should not be live yet while A is still loading");

			// A を完了させる
			slowSource.TrySetResult(new MockScreenViewInstanceProxy());
			await pushA;
			await pushB;

			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
			Assert.IsInstanceOf<HomeScreenId>(ScreenNavigator.Page.Current);
		});

		[UnityTest]
		public IEnumerator ThreeQueuedPushes_RunInFifoOrder_NotConcurrently() => UniTask.ToCoroutine(async () =>
		{
			// 3 連続で Push が来ても直列実行されることを検証する（FIFO チェーン）。
			// 旧実装では B/C が同じ prevDone をキャプチャして A 完走で両方 resume し、
			// body が並走 + _currentCts が後勝ち上書きされていた。
			var sourceA = new UniTaskCompletionSource<IScreenViewInstance>();
			var sourceB = new UniTaskCompletionSource<IScreenViewInstance>();
			var sourceC = new UniTaskCompletionSource<IScreenViewInstance>();
			var order = new System.Collections.Generic.List<string>();
			var handleA = new TracingHandle(sourceA, () => order.Add("A-load"));
			var handleB = new TracingHandle(sourceB, () => order.Add("B-load"));
			var handleC = new TracingHandle(sourceC, () => order.Add("C-load"));

			var pushA = ScreenNavigator.Page.Push(new ControllableScreenId(handleA),
				new PushOptions { InterruptPriority = InterruptPriority.Queue });
			await UniTask.Yield();
			var pushB = ScreenNavigator.Page.Push(new ControllableScreenId(handleB),
				new PushOptions { InterruptPriority = InterruptPriority.Queue });
			var pushC = ScreenNavigator.Page.Push(new ControllableScreenId(handleC),
				new PushOptions { InterruptPriority = InterruptPriority.Queue });

			// A だけ Load 中。B, C は前を待っている状態。
			await UniTask.Yield();
			Assert.AreEqual(new[] { "A-load" }, order.ToArray(),
				"B/C must not start before A completes");

			// A を完了させる → B が動き出す。C はまだ動かない。
			sourceA.TrySetResult(new MockScreenViewInstanceProxy());
			await pushA;
			await UniTask.Yield();
			Assert.AreEqual(new[] { "A-load", "B-load" }, order.ToArray(),
				"C must not start before B completes");

			sourceB.TrySetResult(new MockScreenViewInstanceProxy());
			await pushB;
			await UniTask.Yield();
			Assert.AreEqual(new[] { "A-load", "B-load", "C-load" }, order.ToArray());

			sourceC.TrySetResult(new MockScreenViewInstanceProxy());
			await pushC;

			Assert.AreEqual(3, ScreenNavigator.Page.History.Count);
			Assert.IsFalse(ScreenNavigator.Page.IsTransitioning);
		});

		[UnityTest]
		public IEnumerator Preempt_CancelsAllPriorPendings_NotJustImmediatePrev() => UniTask.ToCoroutine(async () =>
		{
			// A(走行中, Queue) + B(待機, Queue) + C(Preempt) のシナリオ。
			// 期待: A も B もキャンセルされ、C だけが完走する。
			// 「自分の直前のみキャンセル」だと A は走り続け、A 完走 → B 完走 → 最後に C という挙動になる。
			var sourceA = new UniTaskCompletionSource<IScreenViewInstance>();
			var sourceB = new UniTaskCompletionSource<IScreenViewInstance>();
			var sourceC = new UniTaskCompletionSource<IScreenViewInstance>();
			var ran = new System.Collections.Generic.List<string>();
			var handleA = new TracingHandle(sourceA, () => ran.Add("A"));
			var handleB = new TracingHandle(sourceB, () => ran.Add("B"));
			var handleC = new TracingHandle(sourceC, () => ran.Add("C"));

			var pushA = ScreenNavigator.Page.Push(new ControllableScreenId(handleA),
				new PushOptions { InterruptPriority = InterruptPriority.Queue });
			await UniTask.Yield();
			var pushB = ScreenNavigator.Page.Push(new ControllableScreenId(handleB),
				new PushOptions { InterruptPriority = InterruptPriority.Queue });
			var pushC = ScreenNavigator.Page.Push(new ControllableScreenId(handleC),
				new PushOptions { InterruptPriority = InterruptPriority.Preempt });

			// A はキャンセル、B は body 開始前にキャンセルされて Load を呼ばずに抜けるはず
			try { await pushA; Assert.Fail("pushA should be cancelled"); }
			catch (OperationCanceledException) { }
			try { await pushB; Assert.Fail("pushB should be cancelled"); }
			catch (OperationCanceledException) { }

			// C を完了させる
			sourceC.TrySetResult(new MockScreenViewInstanceProxy());
			await pushC;

			Assert.Contains("A", ran, "A should have started loading before being cancelled");
			Assert.IsFalse(ran.Contains("B"), "B's body must not start (preempted before body)");
			Assert.Contains("C", ran);
			Assert.AreEqual(1, ScreenNavigator.Page.History.Count);
		});

		// ---- テスト用の制御可能 Handle / Identifier ----

		sealed class ControllableHandle : IScreenHandle
		{
			readonly UniTaskCompletionSource<IScreenViewInstance> _source;
			public bool UnloadCalled { get; private set; }

			public ControllableHandle(UniTaskCompletionSource<IScreenViewInstance> source) => _source = source;

			public async UniTask<IScreenViewInstance> Load(IProgress<float> progress, CancellationToken ct)
			{
				using (ct.Register(() => _source.TrySetCanceled(ct)))
				{
					return await _source.Task;
				}
			}

			public UniTask Unload(CancellationToken ct)
			{
				UnloadCalled = true;
				return UniTask.CompletedTask;
			}
		}

		sealed class TracingHandle : IScreenHandle
		{
			readonly UniTaskCompletionSource<IScreenViewInstance> _source;
			readonly Action _onLoadStart;

			public TracingHandle(UniTaskCompletionSource<IScreenViewInstance> source, Action onLoadStart)
			{
				_source = source;
				_onLoadStart = onLoadStart;
			}

			public async UniTask<IScreenViewInstance> Load(IProgress<float> progress, CancellationToken ct)
			{
				_onLoadStart?.Invoke();
				using (ct.Register(() => _source.TrySetCanceled(ct)))
				{
					return await _source.Task;
				}
			}

			public UniTask Unload(CancellationToken ct) => UniTask.CompletedTask;
		}

		sealed record ControllableScreenId(IScreenHandle Handle) : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => Handle;
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
		}

		sealed class NullPresenter : IScreenPresenter { }

		sealed class MockScreenViewInstanceProxy : IScreenViewInstance
		{
			public void SetActive(bool active) { }
			public void SetParent(Transform parent) { }
			public T As<T>() where T : class => null;
		}

		// ---- ヘルパー ----

		static IScreenContainer NewContainer(string name)
		{
			var go = new GameObject(name);
			return go.AddComponent<ScreenContainer>();
		}

		static void DestroyContainer(IScreenContainer container)
		{
			if (container is MonoBehaviour mb && mb != null) UnityEngine.Object.DestroyImmediate(mb.gameObject);
		}

		static ScreenLayerConfig NewLayerConfig(IScreenContainer container) => new()
		{
			Container = container,
			DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
			StackMode = StackMode.Cover,
			StackInputPolicy = StackInputPolicy.BlockUnderlying,
			DefaultModal = true,
		};
	}
}
