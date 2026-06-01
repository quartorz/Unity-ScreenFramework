using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	public interface IScreenNavigator
	{
		IScreenHistory History { get; }
		IScreenIdentifier Current { get; }
		bool IsTransitioning { get; }

		UniTask Push(IScreenIdentifier id, PushOptions opt = default, CancellationToken ct = default);
		/// <summary>
		/// 結果を返すダイアログ向け Push。エントリが閉じるまで await される。
		/// 結果未書き込みで閉じた場合は default(TResult)、preempt や DismissAll 等で
		/// 自分のエントリが破棄された場合は OperationCanceledException。
		/// </summary>
		UniTask<TResult> PushAndAwait<TResult>(ScreenIdentifier<TResult> id, PushOptions opt = default, CancellationToken ct = default)
			where TResult : IScreenData;
		UniTask Pop(PopOptions opt = default, CancellationToken ct = default);
		UniTask Replace(IScreenIdentifier id, ReplaceOptions opt = default, CancellationToken ct = default);
		UniTask Change(IScreenIdentifier id, ChangeOptions opt = default, CancellationToken ct = default);
		UniTask Reset(IScreenIdentifier id, ResetOptions opt = default, CancellationToken ct = default);
		UniTask PopTo(Func<IScreenIdentifier, bool> predicate, PopToOptions opt = default, CancellationToken ct = default);

		UniTask DismissAll(CancellationToken ct = default);

		event Action<ScreenTransitionEvent> OnTransitionStart;
		event Action<ScreenTransitionEvent> OnTransitionEnd;
	}
}
