using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Sample;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests
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
			var registry = new SampleRegistry(
				useMockViews: true,
				gacha: new MockGachaService(),
				user: new MockUserService(),
				profile: new MockProfileService(),
				master: new MockMasterService());
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
			DestroyContainer(_pageContainer);
			// 他は親ごと残るが SetUp で上書きされる
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
			DefaultTransition = ImmediateTransition.Instance,
		};
	}
}
