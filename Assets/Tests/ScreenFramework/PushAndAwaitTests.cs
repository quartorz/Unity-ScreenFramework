using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using ScreenFramework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.ScreenFramework
{
	/// <summary>
	/// PushAndAwait&lt;TResult&gt; のうち、<b>MBT 語彙外</b>の側面だけ:
	/// Stack mode での複数 awaiter の混線防止、KeepOnCover での dialog-from-dialog、
	/// suspended のまま中間 Close される場合の last-chance 配送、待機部に ct が効かない仕様。
	/// 正常 Pop での配送（SetResult / default）と DestroyOnCover 上書きでの OCE 決着は、
	/// モデルベーステスト（<c>ModelBased/</c>）の P4 が網羅するため引退した
	/// （2026-06-13。docs/MODEL-BASED-TESTING.md の引退節）。
	/// </summary>
	public sealed class PushAndAwaitTests
	{
		IScreenContainer _pageContainer;

		[SetUp]
		public void SetUp()
		{
			_pageContainer = NewContainer("PageRoot");
			var setup = new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer),
				Dialog = NewLayer(NewContainer("DlgRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			};
			ScreenNavigator.Initialize(new TestServices(), setup);
		}

		[TearDown]
		public void TearDown()
		{
			// 再 Initialize 例外ガード（既初期化なら throw）があるので、各テスト後に静的参照を畳む。
			ScreenNavigator.Shutdown().Forget();
			if (_pageContainer is MonoBehaviour mb && mb != null)
				Object.DestroyImmediate(mb.gameObject);
		}

		[UnityTest]
		public IEnumerator ConcurrentAwaits_DontMix() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Shutdown();
			
			// 同じ型 (EchoResult) を返す dialog を Stack mode で並行に開いて区別できることを確認
			// この test では同じ Page layer を Stack mode に切り替えて 2 枚積む
			Object.DestroyImmediate(((MonoBehaviour)_pageContainer).gameObject);
			_pageContainer = NewContainer("PageRoot");
			var setup = new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer, stack: StackMode.Stack),
				Dialog = NewLayer(NewContainer("DlgRoot")),
				SystemDialog = NewLayer(NewContainer("SysRoot")),
			};
			ScreenNavigator.Initialize(new TestServices(), setup);

			await ScreenNavigator.Page.Push(new PlainScreenId());
			var taskA = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("AAA"));
			var taskB = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("BBB"));
			await UniTask.Yield();

			// 上 (B) から閉じる
			await ScreenNavigator.Page.Pop();
			var b = await taskB;
			Assert.AreEqual("BBB", b.Text);

			await ScreenNavigator.Page.Pop();
			var a = await taskA;
			Assert.AreEqual("AAA", a.Text);
		});

		// =====================================================================
		// 項目 5: Dialog レイヤーの DestroyOnCover × PushAndAwait
		// =====================================================================

		[Test]
		public async Task DialogFromDialog_KeepOnCover_AwaiterSurvivesUntilOwnPop()
		{
			await ScreenNavigator.Shutdown();

			// Dialog を Cover + KeepOnCover にすると「ダイアログからダイアログ」が成立する。
			// 下のダイアログは Suspend され、上のダイアログを Pop すると下の awaiter は
			// 自分が Pop されるときに正規 resolve される。
			Object.DestroyImmediate(((MonoBehaviour)_pageContainer).gameObject);
			_pageContainer = NewContainer("PageRoot");
			var dialogContainer = NewContainer("DlgRoot2");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer),
				Dialog = new ScreenLayerConfig
				{
					Container = dialogContainer,
					DefaultCacheMode = ScreenCacheMode.KeepOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
				},
				SystemDialog = NewLayer(NewContainer("SysRoot2")),
			});

			await ScreenNavigator.Dialog.Push(new PlainScreenId());
			var awaitA = ScreenNavigator.Dialog.PushAndAwait(new EchoDialogId("A"));
			await UniTask.Yield();

			await ScreenNavigator.Dialog.Push(new PlainScreenId());
			await ScreenNavigator.Dialog.Pop();

			await ScreenNavigator.Dialog.Pop();
			var result = await awaitA;
			Assert.IsNotNull(result);
			Assert.AreEqual("A", result.Text);
		}

		[Test]
		public async Task SuspendedDialog_ClosedWithoutResume_StillDeliversResult()
		{
			await ScreenNavigator.Shutdown();

			// KeepOnCover: 結果確定済みのダイアログ A が上に覆われて suspend され、
			// Resume を挟まずに参照 Close される。suspended の破棄では Exit hook が走らないため、
			// 結果は OnAfterUnload（最後の書き込みチャンス）経由で届く必要がある。
			Object.DestroyImmediate(((MonoBehaviour)_pageContainer).gameObject);
			_pageContainer = NewContainer("PageRoot");
			ScreenNavigator.Initialize(new TestServices(), new ScreenLayerSetup
			{
				Page = NewLayer(_pageContainer),
				Dialog = new ScreenLayerConfig
				{
					Container = NewContainer("DlgRoot3"),
					DefaultCacheMode = ScreenCacheMode.KeepOnCover,
					StackMode = StackMode.Cover,
					StackInputPolicy = StackInputPolicy.BlockUnderlying,
					DefaultModal = true,
				},
				SystemDialog = NewLayer(NewContainer("SysRoot3")),
			});

			var task = ScreenNavigator.Dialog.PushAndAwait(new EchoDialogId("kept"));
			await UniTask.Yield();
			var entry = ScreenNavigator.Dialog.FindEntry<EchoDialogPresenter>();
			Assert.IsNotNull(entry, "前提: ダイアログ A が開いている");

			await ScreenNavigator.Dialog.Push(new PlainScreenId()); // A は suspend される
			await entry.Close();                                    // Resume なしの中間 Close

			var result = await task;
			Assert.IsNotNull(result, "suspended のまま閉じられても結果は届く");
			Assert.AreEqual("kept", result.Text);
		}

		// =====================================================================
		// 項目 6: PushAndAwait の待機部に ct は効かない(仕様)
		// =====================================================================

		[Test]
		public async Task ExternalCt_DoesNotCancelWaitPhase_ByDesign()
		{
			// 仕様: ct は Push フェーズ(ロールバック可能ゾーン)のみ作用する。
			// Push が「コミット」された後の結果待ちフェーズは ct で抜けない。
			// 抜けたいときはダイアログを Pop するか、上位で別遷移を発行して preempt する。
			await ScreenNavigator.Page.Push(new PlainScreenId());

			using var cts = new CancellationTokenSource();
			var task = ScreenNavigator.Page.PushAndAwait(new EchoDialogId(text: null), default, cts.Token);

			var done = false;
			OperationCanceledException caughtOce = null;
			UniTask.Void(async () =>
			{
				try { await task; }
				catch (OperationCanceledException e) { caughtOce = e; }
				done = true;
			});

			for (var i = 0; i < 5; i++) await UniTask.Yield();
			Assert.IsFalse(done, "Push 完了後、SetResult を待っている状態のはず");

			cts.Cancel();
			for (var i = 0; i < 10; i++) await UniTask.Yield();
			Assert.IsFalse(done, "ct Cancel しても wait phase は抜けない仕様");

			await ScreenNavigator.Page.Pop();
			for (var i = 0; i < 5; i++) await UniTask.Yield();
			Assert.IsTrue(done, "Pop で wait phase が解決する");
			Assert.IsNull(caughtOce, "正常 Pop なので OCE は出ない");
		}

		// ---- ヘルパー ----

		static IScreenContainer NewContainer(string name)
			=> new GameObject(name).AddComponent<ScreenContainer>();

		static ScreenLayerConfig NewLayer(IScreenContainer container, StackMode stack = StackMode.Cover) => new()
		{
			Container = container,
			DefaultCacheMode = ScreenCacheMode.DestroyOnCover,
			StackMode = stack,
			StackInputPolicy = StackInputPolicy.BlockUnderlying,
			DefaultModal = true,
		};

		sealed class TestServices : ScreenServices
		{
			public TestServices() : base(useMockViews: true) { }
		}

		public sealed class EchoResult : INavigationData
		{
			public string Text;
		}

		// 「OnAfterLoad で text != null なら SetResult し、その後すぐ閉じる」挙動
		sealed class EchoDialogPresenter : DialogPresenter<object, object, EchoResult>
		{
			readonly string _text;
			public EchoDialogPresenter(string text) { _text = text; }

			protected override UniTask OnAfterLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
			{
				if (_text != null) SetResult(new EchoResult { Text = _text });
				return UniTask.CompletedTask;
			}
		}

		sealed record EchoDialogId(string text) : ScreenIdentifier<EchoResult>
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new EchoDialogPresenter(text);
		}

		sealed record PlainScreenId : ScreenIdentifier
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new InstantHandle();
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new NullPresenter();
		}

		sealed class InstantHandle : IScreenHandle
		{
			public UniTask<IScreenViewInstance> Load(Transform stagingParent, IProgress<float> p, CancellationToken c)
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
