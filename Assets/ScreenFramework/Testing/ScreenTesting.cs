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
	/// await presenter.OnAfterLoad(ScreenTesting.ViewOf(mockView), ScreenTesting.EmptyReader, default);
	/// mockView.RaiseOnGoProfileClicked();
	/// </code>
	/// </summary>
	public static class ScreenTesting
	{
		/// <summary>
		/// 任意のオブジェクト（MockView など）を IScreenViewInstance として包む。
		/// SetActive / SetParent は no-op、As&lt;T&gt; は内部オブジェクトの as 変換を返す。
		/// </summary>
		public static IScreenViewInstance ViewOf(object instance)
			=> new TestViewInstance(instance ?? throw new ArgumentNullException(nameof(instance)));

		/// <summary>空のリーダー（Push payload なしの状況）。</summary>
		public static IScreenDataReader EmptyReader => EmptyScreenDataReader.Instance;

		/// <summary>
		/// 1 個の IScreenData を含むリーダーを作る。OnBeforeEnter テスト等で。
		/// </summary>
		public static IScreenDataReader ReaderWith(IScreenData data)
		{
			var store = new ScreenDataStore();
			store.WriteUntyped(data);
			return store;
		}

		/// <summary>
		/// OnBeforeExit / OnAfterExit テスト用。Presenter が書き込んだ値を後から
		/// readerView から読み返す。
		/// </summary>
		public static IScreenDataWriter NewWriter(out IScreenDataReader readerView)
		{
			var store = new ScreenDataStore();
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
		/// OnBeforeLoad → OnAfterLoad → OnBeforeEnter → OnAfterEnter。
		/// <paramref name="reader"/> は OnBeforeLoad / OnBeforeEnter に渡される push payload 相当。
		/// 任意のフェーズで例外が出れば呼び出し側に伝播する（実機の挙動と同じ）。
		/// </summary>
		public static async UniTask PushAsync(
			IScreenPresenter presenter,
			object view,
			IScreenDataReader reader = null,
			CancellationToken ct = default)
		{
			if (presenter == null) throw new ArgumentNullException(nameof(presenter));
			if (view == null) throw new ArgumentNullException(nameof(view));
			reader ??= EmptyScreenDataReader.Instance;

			await presenter.OnBeforeLoad(reader, ct);
			await presenter.OnAfterLoad(ViewOf(view), reader, ct);
			await presenter.OnBeforeEnter(reader, ct);
			await presenter.OnAfterEnter(EmptyScreenDataReader.Instance, ct);
		}

		/// <summary>
		/// Pop 時のライフサイクル一式を実機 Navigator と同順で呼ぶ。
		/// OnBeforeExit → OnAfterExit → OnAfterUnload。
		/// 戻り値は Presenter が exit 時に書き込んだ値を読み返すための reader。
		/// </summary>
		public static async UniTask<IScreenDataReader> PopAsync(
			IScreenPresenter presenter,
			CancellationToken ct = default)
		{
			if (presenter == null) throw new ArgumentNullException(nameof(presenter));
			var store = new ScreenDataStore();
			var writer = (IScreenDataWriter)store;

			await presenter.OnBeforeExit(writer, ct);
			await presenter.OnAfterExit(writer, ct);
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
		}
	}
}
