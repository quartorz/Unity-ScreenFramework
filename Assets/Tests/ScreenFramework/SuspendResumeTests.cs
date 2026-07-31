using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// IScreenPresenter.OnSuspend / OnResume が
	/// StackMode と CacheMode の組合せで正しく発火することを検証する。
	/// </summary>
	public sealed class SuspendResumeTests
	{
		[UnityTest]
		public IEnumerator Cover_KeepOnCover_FiresSuspendOnCover_AndResumeOnPop() => UniTask.ToCoroutine(async () =>
		{
			var lowerPresenter = new RecordingPresenter("A");
			var upperPresenter = new RecordingPresenter("B");

			SetupNavigator(StackMode.Cover, ScreenCacheMode.KeepOnCover);

			await ScreenNavigator.Page.Push(new RecScreenId(lowerPresenter));
			await ScreenNavigator.Page.Push(new RecScreenId(upperPresenter));

			// A は Suspend されているはず（Cover + Keep）
			CollectionAssert.Contains(lowerPresenter.Events, "Suspend");
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "AfterUnload");

			await ScreenNavigator.Page.Pop();

			// Pop 後に A.Resume が呼ばれる
			CollectionAssert.Contains(lowerPresenter.Events, "Resume");
		});

		[UnityTest]
		public IEnumerator Cover_DestroyOnCover_DoesNotSuspend_AndDoesNotResume() => UniTask.ToCoroutine(async () =>
		{
			var lowerPresenter = new RecordingPresenter("A");
			var upperPresenter = new RecordingPresenter("B");

			SetupNavigator(StackMode.Cover, ScreenCacheMode.DestroyOnCover);

			await ScreenNavigator.Page.Push(new RecScreenId(lowerPresenter));
			await ScreenNavigator.Page.Push(new RecScreenId(upperPresenter));

			// Destroy された A は Suspend ではなく Unload
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "Suspend");
			CollectionAssert.Contains(lowerPresenter.Events, "AfterUnload");

			await ScreenNavigator.Page.Pop();

			// Resume は呼ばれない（再ロードされて別 Presenter なので）
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "Resume");
		});

		[UnityTest]
		public IEnumerator Stack_DoesNotSuspendLower_AndDoesNotResumeOnPop() => UniTask.ToCoroutine(async () =>
		{
			var lowerPresenter = new RecordingPresenter("A");
			var upperPresenter = new RecordingPresenter("B");

			SetupNavigator(StackMode.Stack, ScreenCacheMode.KeepOnCover);

			await ScreenNavigator.Page.Push(new RecScreenId(lowerPresenter));
			await ScreenNavigator.Page.Push(new RecScreenId(upperPresenter));

			// Stack なので Suspend は発火しない（Exit 系も発火しない）
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "Suspend");
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "BeforeHide");
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "AfterHide");

			await ScreenNavigator.Page.Pop();

			// Pop 後も Resume は発火しない（そもそも Suspend してない）
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "Resume");
		});

		[UnityTest]
		public IEnumerator PushCachePolicyOverride_KeepsThatScreen_EvenWhenLayerDestroys() => UniTask.ToCoroutine(async () =>
		{
			var lowerPresenter = new RecordingPresenter("A");
			var upperPresenter = new RecordingPresenter("B");

			SetupNavigator(StackMode.Cover, ScreenCacheMode.DestroyOnCover); // レイヤー既定は破棄

			// A 自身を「覆われても保持」で Push する。覆う側ではなく A の指定が効くのが正。
			await ScreenNavigator.Page.Push(new RecScreenId(lowerPresenter),
				new PushOptions { CachePolicyOverride = ScreenCacheMode.KeepOnCover });
			await ScreenNavigator.Page.Push(new RecScreenId(upperPresenter));

			CollectionAssert.Contains(lowerPresenter.Events, "Suspend");
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "AfterUnload");

			await ScreenNavigator.Page.Pop();
			CollectionAssert.Contains(lowerPresenter.Events, "Resume");
		});

		[UnityTest]
		public IEnumerator CoveringPushOverride_DoesNotLeakToCoveredScreen() => UniTask.ToCoroutine(async () =>
		{
			var lowerPresenter = new RecordingPresenter("A");
			var upperPresenter = new RecordingPresenter("B");

			SetupNavigator(StackMode.Cover, ScreenCacheMode.KeepOnCover); // レイヤー既定は保持

			await ScreenNavigator.Page.Push(new RecScreenId(lowerPresenter));
			// 覆う側 B が DestroyOnCover を指定しても、A の運命は A 自身の方針（Keep）で決まる。
			await ScreenNavigator.Page.Push(new RecScreenId(upperPresenter),
				new PushOptions { CachePolicyOverride = ScreenCacheMode.DestroyOnCover });

			CollectionAssert.Contains(lowerPresenter.Events, "Suspend", "覆う側の override は覆われる画面に漏れない");
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "AfterUnload");
		});

		// ---- セットアップ ----

		IScreenContainer _pageContainer;

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード（既初期化なら throw）があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			if (_pageContainer is MonoBehaviour mb && mb != null)
				Object.DestroyImmediate(mb.gameObject);
		}

		void SetupNavigator(StackMode stack, ScreenCacheMode cache)
		{
			_pageContainer = NewContainer("PageRoot");
			var setup = new ScreenLayerSetup
			{
				Page = new ScreenLayerConfig
				{
					Container = _pageContainer,
					DefaultCacheMode = cache,
					StackMode = stack,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
				},
				Dialog = new ScreenLayerConfig { Container = NewContainer("DlgRoot") },
				SystemDialog = new ScreenLayerConfig { Container = NewContainer("SysRoot") },
			};
			ScreenNavigator.Initialize(new TestServices(), setup);
		}

		static IScreenContainer NewContainer(string name)
			=> new GameObject(name).AddComponent<ScreenContainer>();

		// ---- テスト用の Presenter / Identifier / Services ----

		sealed class TestServices : ScreenServices
		{
			public TestServices() : base(useMockViews: true) { }
		}

		sealed class RecordingPresenter : IScreenPresenter
		{
			public string Tag { get; }
			public List<string> Events { get; } = new();
			public RecordingPresenter(string tag) { Tag = tag; }

			public UniTask OnBeforeLoad(INavigationDataReader r, ITransitionContext ctx, CancellationToken c) { Events.Add("BeforeLoad"); return UniTask.CompletedTask; }
			public UniTask OnAfterLoad(IScreenViewInstance v, INavigationDataReader r, ITransitionContext ctx, CancellationToken c) { Events.Add("AfterLoad"); return UniTask.CompletedTask; }
			public UniTask OnBeforeShow(INavigationDataReader r, ITransitionContext ctx, CancellationToken c) { Events.Add("BeforeShow"); return UniTask.CompletedTask; }
			public UniTask OnAfterShow(INavigationDataReader r, ITransitionContext ctx, CancellationToken c) { Events.Add("AfterShow"); return UniTask.CompletedTask; }
			public UniTask OnBeforeHide(INavigationDataWriter w, ITransitionContext ctx, CancellationToken c) { Events.Add("BeforeHide"); return UniTask.CompletedTask; }
			public UniTask OnAfterHide(INavigationDataWriter w, ITransitionContext ctx, CancellationToken c) { Events.Add("AfterHide"); return UniTask.CompletedTask; }
			public UniTask OnSuspend(CancellationToken c) { Events.Add("Suspend"); return UniTask.CompletedTask; }
			public UniTask OnResume(CancellationToken c) { Events.Add("Resume"); return UniTask.CompletedTask; }
			public UniTask OnAfterUnload(INavigationDataWriter w, CancellationToken c) { Events.Add("AfterUnload"); return UniTask.CompletedTask; }
		}

		sealed record RecScreenId(IScreenPresenter Presenter) : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => Presenter;
		}

		sealed class InstantHandle : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(Transform stagingParent, System.IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(new NopView());
			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		sealed class NopView : IScreenViewInstance
		{
			public void SetActive(bool active) { }
			public void SetParent(Transform parent) { }
			public T As<T>() where T : class => null;
			public void ApplyCanvasSorting(Camera camera, int sortingLayerId, int order) { }
		}
	}
}
