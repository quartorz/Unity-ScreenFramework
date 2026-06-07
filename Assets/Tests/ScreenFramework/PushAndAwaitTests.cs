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
	/// PushAndAwait&lt;TResult&gt; が DialogPresenter.SetResult の値を受け取り、
	/// preempt / DismissAll で OCE になることを検証する。
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
			if (_pageContainer is MonoBehaviour mb && mb != null)
				Object.DestroyImmediate(mb.gameObject);
		}

		[UnityTest]
		public IEnumerator ReturnsValue_WhenDialogSetsResult() => UniTask.ToCoroutine(async () =>
		{
			// dialog を 1 枚積めるよう先に下を 1 枚 Push
			await ScreenNavigator.Page.Push(new PlainScreenId());

			var task = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("hello"));

			// 別タスクで Pop を呼んで dialog を閉じさせる
			await UniTask.Yield();
			await ScreenNavigator.Page.Pop();

			var result = await task;
			Assert.IsNotNull(result);
			Assert.AreEqual("hello", result.Text);
		});

		[UnityTest]
		public IEnumerator ReturnsDefault_WhenDialogClosesWithoutSetResult() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new PlainScreenId());

			var task = ScreenNavigator.Page.PushAndAwait(new EchoDialogId(text: null /* SetResult を呼ばない */));

			await UniTask.Yield();
			await ScreenNavigator.Page.Pop();

			var result = await task;
			Assert.IsNull(result, "SetResult を呼ばないままだと default が返る");
		});

		[UnityTest]
		public IEnumerator Throws_WhenPreemptedByNextPush() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new PlainScreenId());

			// SlowDialog はロード中に詰まる。preempt しやすい状態を作る
			var slowSource = new UniTaskCompletionSource<IScreenViewInstance>();
			var task = ScreenNavigator.Page.PushAndAwait(new SlowDialogId(slowSource));

			await UniTask.Yield();

			// 別 Push で preempt
			await ScreenNavigator.Page.Push(new PlainScreenId());

			OperationCanceledException caught = null;
			try { await task; }
			catch (OperationCanceledException e) { caught = e; }
			Assert.IsNotNull(caught, "preempt されたら OperationCanceledException");

			// 取り残しの slowSource は最後に閉じる
			slowSource.TrySetResult(new NopView());
			await UniTask.Yield();
		});

		[UnityTest]
		public IEnumerator Throws_WhenDismissedByDismissAll() => UniTask.ToCoroutine(async () =>
		{
			await ScreenNavigator.Page.Push(new PlainScreenId());
			var task = ScreenNavigator.Page.PushAndAwait(new EchoDialogId("x"));
			await UniTask.Yield();

			await ScreenNavigator.Page.DismissAll();

			OperationCanceledException caught = null;
			try { await task; }
			catch (OperationCanceledException e) { caught = e; }
			Assert.IsNotNull(caught, "DismissAll で破棄されたら OCE");
		});

		[UnityTest]
		public IEnumerator ConcurrentAwaits_DontMix() => UniTask.ToCoroutine(async () =>
		{
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
		public async Task DialogFromDialog_DestroyOnCover_AwaiterDies_BehaviorSpec()
		{
			// フレームワークの挙動仕様: Cover + DestroyOnCover の設定下では、
			// PushAndAwait 中のダイアログ A 上に B を Push すると、A の awaiter は
			// TrySetCanceled されて OCE で死ぬ。これは仕様通り。
			// 「ダイアログからダイアログ」を成立させたい場合は KeepOnCover を使うこと
			// (下の DialogFromDialog_KeepOnCover_AwaiterSurvivesUntilOwnPop 参照)。
			await ScreenNavigator.Dialog.Push(new PlainScreenId());
			var awaitA = ScreenNavigator.Dialog.PushAndAwait(new EchoDialogId("A"));
			await UniTask.Yield();

			await ScreenNavigator.Dialog.Push(new PlainScreenId());

			OperationCanceledException oce = null;
			try { await awaitA; }
			catch (OperationCanceledException e) { oce = e; }

			Assert.IsNotNull(oce, "DestroyOnCover では下の awaiter は OCE で死ぬ仕様");
		}

		[Test]
		public async Task DialogFromDialog_KeepOnCover_AwaiterSurvivesUntilOwnPop()
		{
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
					DefaultTransition = ImmediateTransition.Instance,
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
			DefaultTransition = ImmediateTransition.Instance,
		};

		sealed class TestServices : ScreenServices
		{
			public TestServices() : base(useMockViews: true) { }
		}

		public sealed class EchoResult : IScreenData
		{
			public string Text;
		}

		// 「OnAfterLoad で text != null なら SetResult し、その後すぐ閉じる」挙動
		sealed class EchoDialogPresenter : DialogPresenter<object, object, EchoResult>
		{
			readonly string _text;
			public EchoDialogPresenter(string text) { _text = text; }

			protected override UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
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

		sealed record SlowDialogId(UniTaskCompletionSource<IScreenViewInstance> Source) : ScreenIdentifier<EchoResult>
		{
			public override IScreenHandle CreateHandle(ScreenServices s) => new SlowHandle(Source);
			public override IScreenPresenter CreatePresenter(ScreenServices s) => new EchoDialogPresenter("never");
		}

		sealed class SlowHandle : IScreenHandle
		{
			readonly UniTaskCompletionSource<IScreenViewInstance> _src;
			public SlowHandle(UniTaskCompletionSource<IScreenViewInstance> src) { _src = src; }
			public async UniTask<IScreenViewInstance> Load(IProgress<float> p, CancellationToken ct)
			{
				using (ct.Register(() => _src.TrySetCanceled(ct))) return await _src.Task;
			}
			public UniTask Unload(CancellationToken ct) => UniTask.CompletedTask;
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
