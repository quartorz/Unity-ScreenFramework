using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// Stack モードでの Modal フラグ＋StackInputPolicy による
	/// 入力遮蔽ブロッカー (ScreenFramework.ModalBlocker) の生成・破棄を検証する。
	/// </summary>
	public sealed class ModalBlockerTests
	{
		const string BlockerName = "ScreenFramework.ModalBlocker";

		IScreenContainer _pageContainer;

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード（既初期化なら throw）があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			if (_pageContainer is MonoBehaviour mb && mb != null)
				Object.DestroyImmediate(mb.gameObject);
		}

		// CreatesBlocker_OnSecondPush / DestroysBlocker_OnPop / Cover_NeverCreatesBlocker /
		// RestoredDormantRow_GetsBlocker はモデルベーステストの P10（blocker 個数）に移行し引退した
		// （ModelBasedTests の Pinned_StackMode_* + 全 seed の P10 検査）。

		[UnityTest]
		public IEnumerator Stack_Block_ModalOverrideFalse_SkipsBlocker() => UniTask.ToCoroutine(async () =>
		{
			SetupNavigator(StackMode.Stack, StackInputPolicy.BlockUnderlying, defaultModal: true);

			await ScreenNavigator.Page.Push(new DummyScreenId(1));
			await ScreenNavigator.Page.Push(new DummyScreenId(2),
				new PushOptions { ModalOverride = false });

			Assert.AreEqual(0, CountBlockers(), "ModalOverride=false で blocker を作らない");
		});

		[UnityTest]
		public IEnumerator Stack_PassThrough_NeverCreatesBlocker() => UniTask.ToCoroutine(async () =>
		{
			SetupNavigator(StackMode.Stack, StackInputPolicy.PassThrough, defaultModal: true);

			await ScreenNavigator.Page.Push(new DummyScreenId(1));
			await ScreenNavigator.Page.Push(new DummyScreenId(2));
			await ScreenNavigator.Page.Push(new DummyScreenId(3));

			Assert.AreEqual(0, CountBlockers(), "PassThrough なら Modal でも blocker を作らない");
		});

		[UnityTest]
		public IEnumerator Stack_Block_Modal_ReplaceSwapsBlocker() => UniTask.ToCoroutine(async () =>
		{
			SetupNavigator(StackMode.Stack, StackInputPolicy.BlockUnderlying, defaultModal: true);

			await ScreenNavigator.Page.Push(new DummyScreenId(1));
			await ScreenNavigator.Page.Push(new DummyScreenId(2));
			Assert.AreEqual(1, CountBlockers());

			await ScreenNavigator.Page.Replace(new DummyScreenId(99));
			Assert.AreEqual(1, CountBlockers(), "Replace 後も Modal なので 1 個維持");

			await ScreenNavigator.Page.Replace(new DummyScreenId(100),
				new ReplaceOptions { ModalOverride = false });
			Assert.AreEqual(0, CountBlockers(), "Modal=false に Replace すれば消える");
		});

		// ---- ヘルパー ----

		void SetupNavigator(StackMode stack, StackInputPolicy policy, bool defaultModal)
		{
			_pageContainer = NewContainer("PageRoot");
			var setup = new ScreenLayerSetup
			{
				Page = new ScreenLayerConfig
				{
					Container = _pageContainer,
					DefaultCacheMode = ScreenCacheMode.KeepOnCover,
					StackMode = stack,
					StackInputPolicy = policy,
					DefaultModal = defaultModal,
				},
				Dialog = new ScreenLayerConfig { Container = NewContainer("DlgRoot") },
				SystemDialog = new ScreenLayerConfig { Container = NewContainer("SysRoot") },
			};
			ScreenNavigator.Initialize(new TestServices(), setup);
		}

		int CountBlockers()
		{
			var root = _pageContainer.Root;
			var count = 0;
			for (var i = 0; i < root.childCount; i++)
			{
				if (root.GetChild(i).name == BlockerName) count++;
			}
			return count;
		}

		static IScreenContainer NewContainer(string name)
			=> new GameObject(name).AddComponent<ScreenContainer>();

		sealed class TestServices : ScreenServices
		{
			public TestServices() : base(useMockViews: true) { }
		}

		sealed record DummyScreenId(int N) : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
		}

		sealed class InstantHandle : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(Transform stagingParent, System.IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(new NopView());
			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		sealed class NullPresenter : IScreenPresenter { }

		sealed class NopView : IScreenViewInstance
		{
			public void SetActive(bool active) { }
			public void SetParent(Transform parent) { }
			public T As<T>() where T : class => null;
			public void ApplyCanvasSorting(Camera camera, int sortingLayerId, int order) { }
		}
	}
}
