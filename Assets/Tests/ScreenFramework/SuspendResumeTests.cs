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
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "BeforeExit");
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "AfterExit");

			await ScreenNavigator.Page.Pop();

			// Pop 後も Resume は発火しない（そもそも Suspend してない）
			CollectionAssert.DoesNotContain(lowerPresenter.Events, "Resume");
		});

		// ---- セットアップ ----

		IScreenContainer _pageContainer;

		[TearDown]
		public void TearDown()
		{
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
					DefaultTransition = ImmediateTransition.Instance,
				},
				Dialog = new ScreenLayerConfig { Container = NewContainer("DlgRoot"), DefaultTransition = ImmediateTransition.Instance },
				SystemDialog = new ScreenLayerConfig { Container = NewContainer("SysRoot"), DefaultTransition = ImmediateTransition.Instance },
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

			public UniTask OnBeforeLoad(IScreenDataReader r, CancellationToken c) { Events.Add("BeforeLoad"); return UniTask.CompletedTask; }
			public UniTask OnAfterLoad(IScreenViewInstance v, IScreenDataReader r, CancellationToken c) { Events.Add("AfterLoad"); return UniTask.CompletedTask; }
			public UniTask OnBeforeEnter(IScreenDataReader r, CancellationToken c) { Events.Add("BeforeEnter"); return UniTask.CompletedTask; }
			public UniTask OnAfterEnter(IScreenDataReader r, CancellationToken c) { Events.Add("AfterEnter"); return UniTask.CompletedTask; }
			public UniTask OnBeforeExit(IScreenDataWriter w, CancellationToken c) { Events.Add("BeforeExit"); return UniTask.CompletedTask; }
			public UniTask OnAfterExit(IScreenDataWriter w, CancellationToken c) { Events.Add("AfterExit"); return UniTask.CompletedTask; }
			public UniTask OnSuspend(CancellationToken c) { Events.Add("Suspend"); return UniTask.CompletedTask; }
			public UniTask OnResume(CancellationToken c) { Events.Add("Resume"); return UniTask.CompletedTask; }
			public UniTask OnAfterUnload(IScreenDataWriter w, CancellationToken c) { Events.Add("AfterUnload"); return UniTask.CompletedTask; }
		}

		sealed record RecScreenId(IScreenPresenter Presenter) : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => Presenter;
		}

		sealed class InstantHandle : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(System.IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(new NopView());
			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		sealed class NopView : IScreenViewInstance
		{
			public void SetActive(bool active) { }
			public void SetParent(Transform parent) { }
			public T As<T>() where T : class => null;
		}
	}
}
