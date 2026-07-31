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
	/// View が IScreenAnimatedView を実装しているとき、
	/// PlayEnter / PlayExit が Navigator のライフサイクル該当箇所で呼ばれることを検証する。
	/// </summary>
	public sealed class AnimatedViewTests
	{
		IScreenContainer _pageContainer;

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード（既初期化なら throw）があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			if (_pageContainer is MonoBehaviour mb && mb != null)
				Object.DestroyImmediate(mb.gameObject);
		}
		
		[SetUp]
		public void SetUp()
		{
			ScreenNavigator.Shutdown().Forget();
		}

		[UnityTest]
		public IEnumerator Push_FiresPlayEnter() => UniTask.ToCoroutine(async () =>
		{
			SetupNavigator(StackMode.Cover);

			var view = new AnimRecordView();
			await ScreenNavigator.Page.Push(new AnimScreenId(view));

			CollectionAssert.Contains(view.Events, "Enter");
			CollectionAssert.DoesNotContain(view.Events, "Exit");
		});

		[UnityTest]
		public IEnumerator Pop_FiresPlayExit_OnTop_AndPlayEnter_OnReappearingBelow() => UniTask.ToCoroutine(async () =>
		{
			SetupNavigator(StackMode.Cover, ScreenCacheMode.KeepOnCover);

			var below = new AnimRecordView();
			var top   = new AnimRecordView();

			await ScreenNavigator.Page.Push(new AnimScreenId(below));
			await ScreenNavigator.Page.Push(new AnimScreenId(top));

			// Cover で覆われたタイミングで below は PlayExit を 1 度発火（visually 消える → Suspend）
			Assert.AreEqual(1, CountEvent(below.Events, "Enter"));
			Assert.AreEqual(1, CountEvent(below.Events, "Exit"));

			await ScreenNavigator.Page.Pop();

			// top は Exit、below は再表示で Enter（2 度目）、Exit は増えない
			CollectionAssert.Contains(top.Events, "Exit");
			Assert.AreEqual(2, CountEvent(below.Events, "Enter"));
			Assert.AreEqual(1, CountEvent(below.Events, "Exit"));
		});

		[UnityTest]
		public IEnumerator Stack_Pop_DoesNot_PlayEnter_OnBelow() => UniTask.ToCoroutine(async () =>
		{
			SetupNavigator(StackMode.Stack);

			var below = new AnimRecordView();
			var top   = new AnimRecordView();

			await ScreenNavigator.Page.Push(new AnimScreenId(below));
			await ScreenNavigator.Page.Push(new AnimScreenId(top));

			Assert.AreEqual(1, CountEvent(below.Events, "Enter"));

			await ScreenNavigator.Page.Pop();

			// Stack なら below は常時 visible だったので Enter は再発火しない
			Assert.AreEqual(1, CountEvent(below.Events, "Enter"));
			// top の Exit は発火
			CollectionAssert.Contains(top.Events, "Exit");
		});

		[UnityTest]
		public IEnumerator Replace_FiresExitOnOld_AndEnterOnNew() => UniTask.ToCoroutine(async () =>
		{
			SetupNavigator(StackMode.Cover);

			var oldView = new AnimRecordView();
			var newView = new AnimRecordView();

			await ScreenNavigator.Page.Push(new AnimScreenId(oldView));
			await ScreenNavigator.Page.Replace(new AnimScreenId(newView));

			CollectionAssert.Contains(oldView.Events, "Exit");
			CollectionAssert.Contains(newView.Events, "Enter");
		});

		// ---- ヘルパー ----

		static int CountEvent(List<string> events, string name)
		{
			var c = 0;
			foreach (var e in events) if (e == name) c++;
			return c;
		}

		void SetupNavigator(StackMode stack, ScreenCacheMode cache = ScreenCacheMode.DestroyOnCover)
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

		sealed class TestServices : ScreenServices
		{
			public TestServices() : base(useMockViews: true) { }
		}

		sealed class AnimRecordView : IScreenAnimatedView
		{
			public List<string> Events { get; } = new();
			public UniTask PlayEnter(CancellationToken ct) { Events.Add("Enter"); return UniTask.CompletedTask; }
			public UniTask PlayExit(CancellationToken ct)  { Events.Add("Exit");  return UniTask.CompletedTask; }
		}

		// 同じ View インスタンスを返す Handle（テストで record 可能にするため）
		sealed class FixedView : IScreenViewInstance
		{
			readonly object _obj;
			public FixedView(object obj) { _obj = obj; }
			public void SetActive(bool active) { }
			public void SetParent(Transform parent) { }
			public T As<T>() where T : class => _obj as T;
			public void ApplyCanvasSorting(Camera camera, int sortingLayerId, int order) { }
		}

		sealed class FixedHandle : IScreenHandle
		{
			readonly object _obj;
			public FixedHandle(object obj) { _obj = obj; }
			public UniTask<IScreenViewInstance> Load(Transform stagingParent, System.IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(new FixedView(_obj));
			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		sealed record AnimScreenId(AnimRecordView View) : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new FixedHandle(View);
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
		}

		sealed class NullPresenter : IScreenPresenter { }
	}
}
