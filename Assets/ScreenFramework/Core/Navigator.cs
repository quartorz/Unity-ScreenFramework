using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <remarks>
	/// <b>hook 内からの同レイヤー遷移に注意</b>: Presenter / Effect の hook（OnAfterShow 等）の中から
	/// <b>同じレイヤー</b>の遷移 API（Push/Pop/Replace/Change/Reset/PopTo/Close）を <c>await</c> すると
	/// <b>恒久デッドロック</b>する。各遷移は直前の遷移の完了を待つ設計（FIFO + preempt）なので、
	/// 新しい遷移は現在の遷移（＝その hook を待っている）の完了を待ち、相互待ちになるため。
	/// hook 内でリダイレクトしたい場合は <c>await</c> せず
	/// <see cref="ScreenNavigatorRedirectExtensions.Redirect(Cysharp.Threading.Tasks.UniTask)"/>
	/// （＝意図を明示した <c>.Forget()</c>）で発行すること（現在の遷移が完了した後に実行される）。
	/// 別レイヤーへの遷移は await して構わない。
	/// <para>
	/// <b>静的参照とシーン寿命</b>: <see cref="ScreenNavigator"/> の静的参照は明示的に
	/// <see cref="ScreenNavigator.Shutdown"/> するまで生き続ける。Navigator が握る画面インスタンスや
	/// <see cref="ScreenLayerConfig.Container"/> はシーン上の GameObject なので、<c>Shutdown</c> せずに
	/// シーンを破棄すると、Navigator が destroy 済みの View を抱えたまま以後の操作が壊れる。
	/// シーン遷移・再初期化の前に必ず <c>await ScreenNavigator.Shutdown()</c> すること。
	/// </para>
	/// </remarks>
	public interface IScreenNavigator
	{
		IScreenHistory History { get; }
		IScreenIdentifier Current { get; }
		bool IsTransitioning { get; }

		/// <summary>スタック最上段のエントリ。空なら null。</summary>
		IScreenEntry TopEntry { get; }

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
		/// 「正常な閉じ方」= 自分が最上段として退場 hook を通って閉じる場合
		/// （Pop / <see cref="Close"/> / PopTo の最終 Pop）は結果が配送される。
		/// それ以外（preempt / Replace・Change による差し替え / DismissAll・Reset の全破棄 /
		/// PopTo の中間としての無音破棄 / 覆われたまま破棄（cover-destroy）/
		/// <see cref="IScreenHistory.Edit"/> による行削除 / <see cref="ScreenNavigator.Shutdown"/>）は
		/// OperationCanceledException で決着し、どの経路でもハングしない。
		/// </para>
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

		/// <summary>現在の最上段を新画面に差し替える。完了後、新画面のエントリを返す（<see cref="Push"/> と対称）。</summary>
		UniTask<IScreenEntry> Replace(IScreenIdentifier id, ReplaceOptions opt = default, CancellationToken ct = default);
		/// <summary>
		/// 下スタックを破棄しつつ最上段を新画面へ差し替える。完了後、新画面のエントリを返す。
		/// 最上段だけが Effect 付きの cross-fade で差し替わり、下スタックは演出なしで黙って破棄される。
		/// Stack モード（複数画面を同時表示するレイヤー）で使うと、見えている下積みも無演出で消えるため、
		/// 主に単一画面を積む Page レイヤー向け。
		/// </summary>
		UniTask<IScreenEntry> Change(IScreenIdentifier id, ChangeOptions opt = default, CancellationToken ct = default);
		/// <summary>全画面を破棄し新画面 1 枚にする。完了後、新画面のエントリを返す。</summary>
		UniTask<IScreenEntry> Reset(IScreenIdentifier id, ResetOptions opt = default, CancellationToken ct = default);
		UniTask PopTo(Func<IScreenIdentifier, bool> predicate, PopToOptions opt = default, CancellationToken ct = default);

		UniTask DismissAll(CancellationToken ct = default);

		event Action<ScreenTransitionEvent> OnTransitionStart;
		event Action<ScreenTransitionEvent> OnTransitionEnd;
	}
}
