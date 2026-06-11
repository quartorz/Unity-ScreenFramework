using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <remarks>
	/// <b>hook 内からの同レイヤー遷移に注意</b>: Presenter / Effect の hook（OnAfterEnter 等）の中から
	/// <b>同じレイヤー</b>の遷移 API（Push/Pop/Replace/Change/Reset/PopTo/Close）を <c>await</c> すると
	/// <b>恒久デッドロック</b>する。各遷移は直前の遷移の完了を待つ設計（FIFO + preempt）なので、
	/// 新しい遷移は現在の遷移（＝その hook を待っている）の完了を待ち、相互待ちになるため。
	/// hook 内でリダイレクトしたい場合は <c>await</c> せず
	/// <see cref="ScreenNavigatorRedirectExtensions.Redirect(Cysharp.Threading.Tasks.UniTask)"/>
	/// （＝意図を明示した <c>.Forget()</c>）で発行すること（現在の遷移が完了した後に実行される）。
	/// 別レイヤーへの遷移は await して構わない。
	/// </remarks>
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
			where TResult : INavigationData;
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
