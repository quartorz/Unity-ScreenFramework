using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;

namespace Tests.Support
{
	/// <summary>
	/// <see cref="MockScreenNavigator"/> でジェネリックな <c>PushAndAwait&lt;TResult&gt;</c> をモックする際の補助。
	/// 生成された <c>PushAndAwaitDelegate</c> を直接実装するのは Call の中で
	/// <c>typeof(TResult)</c> 分岐 + キャストが必要で煩雑になるため、
	/// (Identifier 型, Result 型) ペアを登録するだけで済む API を提供する。
	///
	/// 使い方:
	/// <code>
	/// _dialogNav.SetupPushAndAwait&lt;InputDialogId, InputDialogResult&gt;(id =&gt; new InputDialogResult("New"));
	/// </code>
	/// </summary>
	public static class MockScreenNavigatorExtensions
	{
		public static MockScreenNavigator SetupPushAndAwait<TId, TResult>(
			this MockScreenNavigator nav,
			Func<TId, TResult> handler)
			where TId : ScreenIdentifier<TResult>
			where TResult : IScreenData
		{
			var stub = nav.PushAndAwaitFunc as PushAndAwaitStub
				?? new PushAndAwaitStub();
			stub.Register<TId, TResult>(handler);
			nav.PushAndAwaitFunc = stub;
			return nav;
		}

		public static MockScreenNavigator SetupPushAndAwait<TId, TResult>(
			this MockScreenNavigator nav,
			TResult result)
			where TId : ScreenIdentifier<TResult>
			where TResult : IScreenData
			=> nav.SetupPushAndAwait<TId, TResult>(_ => result);

		/// <summary>
		/// 結果は <c>default(TResult)</c>（参照型なら null）を返しつつ、呼び出しだけ記録したいとき用。
		/// 「キャンセル相当の挙動」を assert したい場面で <see cref="AwaitedIds"/> と組み合わせて使う。
		/// </summary>
		public static MockScreenNavigator TrackPushAndAwait<TId, TResult>(this MockScreenNavigator nav)
			where TId : ScreenIdentifier<TResult>
			where TResult : IScreenData
			=> nav.SetupPushAndAwait<TId, TResult>(_ => default);

		/// <summary>
		/// PushAndAwait された Identifier の履歴を取り出す。assert 用。
		/// </summary>
		public static IReadOnlyList<IScreenIdentifier> AwaitedIds(this MockScreenNavigator nav)
			=> (nav.PushAndAwaitFunc as PushAndAwaitStub)?.Awaited
				?? (IReadOnlyList<IScreenIdentifier>)System.Array.Empty<IScreenIdentifier>();
	}

	sealed class PushAndAwaitStub : MockScreenNavigator.IPushAndAwaitDelegate
	{
		readonly Dictionary<Type, Func<IScreenIdentifier, IScreenData>> _handlers = new();
		public List<IScreenIdentifier> Awaited { get; } = new();

		public void Register<TId, TResult>(Func<TId, TResult> handler)
			where TId : ScreenIdentifier<TResult>
			where TResult : IScreenData
		{
			_handlers[typeof(TId)] = id => handler((TId)id);
		}

		public UniTask<TResult> Call<TResult>(ScreenIdentifier<TResult> id, PushOptions opt, CancellationToken ct)
			where TResult : IScreenData
		{
			Awaited.Add(id);
			if (_handlers.TryGetValue(id.GetType(), out var f))
				return UniTask.FromResult((TResult)f(id));
			return UniTask.FromResult<TResult>(default);
		}
	}
}
