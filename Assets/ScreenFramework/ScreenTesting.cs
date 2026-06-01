using System;
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
	/// mockView.RaiseOnGoDetailClicked();
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
