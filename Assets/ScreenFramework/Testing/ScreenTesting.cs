using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ScreenFramework
{
	/// <summary>
	/// Presenter 単体テスト用のヘルパー。Navigator や GameObject に依存せず、
	/// MockView と Presenter を直接組み合わせてライフサイクルを駆動するための入口。
	///
	/// 使い方:
	/// <code>
	/// var mockView = new MockView.Sample.MockHomeView();
	/// mockView.SetTitleFunc = t => capturedTitle = t;
	/// var presenter = (IScreenPresenter)new HomePresenter();
	/// await ScreenTesting.PushAsync(presenter, mockView);
	/// mockView.RaiseOnGoProfileClicked();
	/// </code>
	/// </summary>
	public static class ScreenTesting
	{
		/// <summary>
		/// テスト用の <see cref="ITransitionContext"/> を 1 個作る。Presenter の hook に渡され、
		/// stage signal（<see cref="ITransitionContext.PublishStage{TStage}"/> /
		/// <see cref="ITransitionContext.WaitForStage{TStage}"/>）の assert に使える。
		/// 例: Push 後に <c>await ctx.WaitForStage&lt;DataReadyStage&gt;(timeout: TimeSpan.FromMilliseconds(1))</c>
		/// が例外を出さなければ「該当 stage が publish された」ことの検証になる（publish 忘れハングの予防）。
		/// </summary>
		public static ITransitionContext NewTransition(
			OperationKind kind = OperationKind.Push,
			IScreenIdentifier from = null,
			IScreenIdentifier to = null)
		{
			var store = new NavigationDataStore();
			return new TransitionContext(kind, from, to, store, store);
		}
		/// <summary>
		/// 任意のオブジェクト（MockView など）を IScreenViewInstance として包む。
		/// SetActive / SetParent は no-op、As&lt;T&gt; は内部オブジェクトの as 変換を返す。
		/// </summary>
		public static IScreenViewInstance ViewOf(object instance)
			=> new TestViewInstance(instance ?? throw new ArgumentNullException(nameof(instance)));

		/// <summary>空のリーダー（Push payload なしの状況）。</summary>
		public static INavigationDataReader EmptyReader => EmptyNavigationDataReader.Instance;

		/// <summary>
		/// 1 個の INavigationData を含むリーダーを作る。OnBeforeShow テスト等で。
		/// </summary>
		public static INavigationDataReader ReaderWith(INavigationData data)
		{
			var store = new NavigationDataStore();
			store.WriteUntyped(data);
			return store;
		}

		/// <summary>
		/// OnBeforeHide / OnAfterHide テスト用。Presenter が書き込んだ値を後から
		/// readerView から読み返す。
		/// </summary>
		public static INavigationDataWriter NewWriter(out INavigationDataReader readerView)
		{
			var store = new NavigationDataStore();
			readerView = store;
			return store;
		}

		/// <summary>
		/// Navigator 経由で生成されない Presenter に <see cref="ScreenServices"/> を手動で注入する。
		/// <see cref="IScreenPresenter.AssignServices"/> を直接呼ぶ。
		/// 戻り値はチェーン用に同じ presenter を返す。
		/// </summary>
		public static TPresenter WithServices<TPresenter>(this TPresenter presenter, ScreenServices services)
			where TPresenter : IScreenPresenter
		{
			presenter.AssignServices(services);
			return presenter;
		}

		/// <summary>
		/// Push 時のライフサイクル一式を実機 Navigator と同順で呼ぶ。
		/// OnInitialize → OnBeforeLoad → OnAfterLoad → OnBeforeShow → OnAfterShow。
		/// <paramref name="reader"/> は OnBeforeLoad / OnBeforeShow / OnAfterShow に渡される push payload 相当。
		/// 任意のフェーズで例外が出れば呼び出し側に伝播する（実機の挙動と同じ）。
		/// </summary>
		public static async UniTask PushAsync(
			IScreenPresenter presenter,
			object view,
			INavigationDataReader reader = null,
			ITransitionContext context = null,
			CancellationToken ct = default)
		{
			if (presenter == null) throw new ArgumentNullException(nameof(presenter));
			if (view == null) throw new ArgumentNullException(nameof(view));
			reader ??= EmptyNavigationDataReader.Instance;
			context ??= NewTransition(OperationKind.Push);

			await presenter.OnInitialize(ct);
			await presenter.OnBeforeLoad(reader, context, ct);
			await presenter.OnAfterLoad(ViewOf(view), reader, context, ct);
			await presenter.OnBeforeShow(reader, context, ct);
			await presenter.OnAfterShow(reader, context, ct);
		}

		/// <summary>
		/// Pop 時のライフサイクル一式を実機 Navigator と同順で呼ぶ。
		/// OnBeforeHide → OnAfterHide → OnAfterUnload。
		/// 戻り値は Presenter が exit 時に書き込んだ値を読み返すための reader。
		/// </summary>
		public static async UniTask<INavigationDataReader> PopAsync(
			IScreenPresenter presenter,
			ITransitionContext context = null,
			CancellationToken ct = default)
		{
			if (presenter == null) throw new ArgumentNullException(nameof(presenter));
			var store = new NavigationDataStore();
			var writer = (INavigationDataWriter)store;
			context ??= NewTransition(OperationKind.Pop);

			await presenter.OnBeforeHide(writer, context, ct);
			await presenter.OnAfterHide(writer, context, ct);
			await presenter.OnAfterUnload(writer, ct);
			return store;
		}

		sealed class TestViewInstance : IScreenViewInstance
		{
			readonly object _obj;
			public TestViewInstance(object obj) { _obj = obj; }
			public void SetActive(bool active) { /* no-op */ }
			public void SetParent(Transform parent) { /* no-op */ }
			public T As<T>() where T : class => _obj as T;
			public void ApplyCanvasSorting(Camera camera, int sortingLayerId, int order) { /* no-op */ }
		}
	}
}
