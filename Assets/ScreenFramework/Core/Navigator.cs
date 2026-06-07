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

		/// <summary>
		/// 画面を Push する。完了後、その画面のエントリを返す。
		/// チュートリアル等で Presenter インスタンスを後から操作したい場合や、
		/// 特定のエントリを <see cref="IScreenEntry.Close"/> で閉じたい場合に保持しておく。
		/// </summary>
		UniTask<IScreenEntry> Push(IScreenIdentifier id, PushOptions opt = default, CancellationToken ct = default);
		/// <summary>
		/// 結果を返すダイアログ向け Push。エントリが閉じるまで await される。
		/// 結果未書き込みで閉じた場合は default(TResult)、preempt や DismissAll 等で
		/// 自分のエントリが破棄された場合は OperationCanceledException。
		/// <para>
		/// <paramref name="ct"/> は Push フェーズ（ロールバック可能ゾーン）にのみ作用する。
		/// Push が完了した後の結果待ちフェーズは ct でキャンセルできない仕様。
		/// 結果待ちを抜けたい場合はダイアログ側を Pop / Close するか、上位レイヤーで
		/// 別の遷移を発行して preempt する（OCE で抜ける）。
		/// </para>
		/// </summary>
		UniTask<TResult> PushAndAwait<TResult>(ScreenIdentifier<TResult> id, PushOptions opt = default, CancellationToken ct = default)
			where TResult : IScreenData;
		UniTask Pop(PopOptions opt = default, CancellationToken ct = default);
		/// <summary>
		/// 指定 Presenter のエントリを閉じる。位置依存の Pop と違い参照で閉じるため、
		/// 競合する遷移と組み合わさっても他のエントリを誤って閉じない。
		/// 既に閉じられている / まだ Push 完了していないときは何もしない。
		/// 履歴の最後の 1 枚でも閉じられる（Pop と違ってガードなし）。
		/// </summary>
		UniTask Close(IScreenPresenter target, PopOptions opt = default, CancellationToken ct = default);

		/// <summary>
		/// スタック上で <typeparamref name="TPresenter"/> 型のエントリを上（最新）から探して返す。
		/// なければ null。複数あれば最も上にあるものを返す。
		/// </summary>
		IScreenEntry FindEntry<TPresenter>() where TPresenter : class, IScreenPresenter;
		UniTask Replace(IScreenIdentifier id, ReplaceOptions opt = default, CancellationToken ct = default);
		UniTask Change(IScreenIdentifier id, ChangeOptions opt = default, CancellationToken ct = default);
		UniTask Reset(IScreenIdentifier id, ResetOptions opt = default, CancellationToken ct = default);
		UniTask PopTo(Func<IScreenIdentifier, bool> predicate, PopToOptions opt = default, CancellationToken ct = default);

		UniTask DismissAll(CancellationToken ct = default);

		event Action<ScreenTransitionEvent> OnTransitionStart;
		event Action<ScreenTransitionEvent> OnTransitionEnd;
	}
}
