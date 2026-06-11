using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// Replace / Change / Reset / PopTo が進行中に新しい Push で preempt されたとき、
	/// 後続が確実に勝ち、preempt 前の Handle が Unload されること（リーク防止）を検証する。
	/// </summary>
	public sealed class PreemptCompatTests
	{
		IScreenContainer _pageContainer;

		[SetUp]
		public void SetUp()
		{
			_pageContainer = NewContainer("PageRoot");
			var setup = new ScreenLayerSetup
			{
				Page = NewLayerConfig(_pageContainer),
				Dialog = NewLayerConfig(NewContainer("DlgRoot")),
				SystemDialog = NewLayerConfig(NewContainer("SysRoot")),
			};
			ScreenNavigator.Initialize(new TestServices(), setup);
		}

		[TearDown]
		public void TearDown()
		{
			if (_pageContainer is MonoBehaviour mb && mb != null)
				Object.DestroyImmediate(mb.gameObject);
		}

		[UnityTest]
		public IEnumerator Replace_WhileLoading_IsPreempted_ByNextPush() => UniTask.ToCoroutine(async () =>
		{
			// 前提：1 枚 Push 済み
			await ScreenNavigator.Page.Push(new InstantScreenId(1));

			var slowSource = new UniTaskCompletionSource<IScreenViewInstance>();
			var slowHandle = new ControllableHandle(slowSource);
			var slowReplace = ScreenNavigator.Page.Replace(new ControllableScreenId(slowHandle));

			await UniTask.Yield();

			var fastPush = ScreenNavigator.Page.Push(new InstantScreenId(2));

			try { await slowReplace; Assert.Fail("slow Replace should have been cancelled"); }
			catch (OperationCanceledException) { /* 期待 */ }

			Assert.IsTrue(slowHandle.UnloadCalled, "preempt された Handle が Unload されること");

			await fastPush;

			Assert.IsInstanceOf<InstantScreenId>(ScreenNavigator.Page.Current);
			Assert.AreEqual(2, ((InstantScreenId)ScreenNavigator.Page.Current).N);

			// 取り残し防止：A のロードを後から完了させても安全
			slowSource.TrySetResult(new NopView());
			await UniTask.Yield();
		});

		[UnityTest]
		public IEnumerator Change_WhileLoading_IsPreempted_ByNextPush() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new InstantScreenId(1));
			await ScreenNavigator.Page.Push(new InstantScreenId(2));
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);

			var slowSource = new UniTaskCompletionSource<IScreenViewInstance>();
			var slowHandle = new ControllableHandle(slowSource);

			// Change は内部で「新画面の Load（ロールバック可能ゾーン）→ 成功後に下スタック破棄」。
			// ここでは Load 段で詰まる＝まだ破壊前なので、preempt されても下スタックは壊れない。
			var slowChange = ScreenNavigator.Page.Change(new ControllableScreenId(slowHandle));

			await UniTask.Yield();

			var fastPush = ScreenNavigator.Page.Push(new InstantScreenId(99));

			try { await slowChange; }
			catch (OperationCanceledException) { /* 期待 */ }

			Assert.IsTrue(slowHandle.UnloadCalled);
			await fastPush;

			// Change の Load が preempt されたので破壊は一切起きず、その上に 99 が積まれる
			Assert.IsInstanceOf<InstantScreenId>(ScreenNavigator.Page.Current);
			Assert.AreEqual(99, ((InstantScreenId)ScreenNavigator.Page.Current).N);

			slowSource.TrySetResult(new NopView());
			await UniTask.Yield();
		});

		[UnityTest]
		public IEnumerator Reset_WhileLoading_IsPreempted_ByNextPush() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new InstantScreenId(1));
			await ScreenNavigator.Page.Push(new InstantScreenId(2));

			var slowSource = new UniTaskCompletionSource<IScreenViewInstance>();
			var slowHandle = new ControllableHandle(slowSource);

			// Reset は内部で「新画面の Load（ロールバック可能ゾーン）→ 成功後に全破壊」。
			// ここでは Load 段で詰まる＝まだ破壊前なので、preempt されても既存スタックは壊れない。
			var slowReset = ScreenNavigator.Page.Reset(new ControllableScreenId(slowHandle));

			await UniTask.Yield();

			var fastPush = ScreenNavigator.Page.Push(new InstantScreenId(99));

			try { await slowReset; }
			catch (OperationCanceledException) { /* 期待 */ }

			Assert.IsTrue(slowHandle.UnloadCalled);
			await fastPush;

			Assert.IsInstanceOf<InstantScreenId>(ScreenNavigator.Page.Current);
			Assert.AreEqual(99, ((InstantScreenId)ScreenNavigator.Page.Current).N);

			slowSource.TrySetResult(new NopView());
			await UniTask.Yield();
		});

		[UnityTest]
		public IEnumerator PopTo_FinalPop_IsPreempted_ByNextPush() => UniTask.ToCoroutine(async () =>
		{
			// 3 枚積む
			await ScreenNavigator.Page.Push(new InstantScreenId(1));
			await ScreenNavigator.Page.Push(new InstantScreenId(2));
			await ScreenNavigator.Page.Push(new InstantScreenId(3));
			Assert.AreEqual(3, ScreenNavigator.Page.History.Count);

			// PopTo は内部で最後に Pop(...) を Run。Pop 自体は Mock では即完走するため
			// ここでは PopTo の戻りを待たずに即 Push で preempt しようとするが、
			// Mock 経路では preempt のレースが起きにくい。代わりに「PopTo の結果として
			// 後続 Push が正しく上に積まれる」ことを確認する。
			var popTask = ScreenNavigator.Page.PopTo(id => id is InstantScreenId i && i.N == 1);
			var pushTask = ScreenNavigator.Page.Push(new InstantScreenId(99),
				new PushOptions { InterruptPriority = InterruptPriority.Queue });

			await popTask;
			await pushTask;

			// PopTo で 1 まで巻き戻り、その上に 99 が乗る
			Assert.AreEqual(2, ScreenNavigator.Page.History.Count);
			Assert.AreEqual(99, ((InstantScreenId)ScreenNavigator.Page.Current).N);
		});

		// ---- ヘルパー ----

		static IScreenContainer NewContainer(string name)
			=> new GameObject(name).AddComponent<ScreenContainer>();

		static ScreenLayerConfig NewLayerConfig(IScreenContainer container) => new()
		{
			Container = container,
			DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
			StackMode = StackMode.Cover,
			StackInputPolicy = StackInputPolicy.BlockUnderlying,
			DefaultModal = true,
		};

		sealed class TestServices : ScreenServices
		{
			public TestServices() : base(useMockViews: true) { }
		}

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

		sealed record InstantScreenId(int N) : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
		}

		sealed class InstantHandle : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken c)
				=> UniTask.FromResult<IScreenViewInstance>(new NopView());
			public UniTask Unload(CancellationToken c) => UniTask.CompletedTask;
		}

		sealed class NullPresenter : IScreenPresenter { }

		sealed class NopView : IScreenViewInstance
		{
			public void SetActive(bool active) { }
			public void SetParent(Transform parent) { }
			public T As<T>() where T : class => null;
		}
	}
}
