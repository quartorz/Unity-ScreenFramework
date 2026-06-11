using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// hook（OnAfterEnter 等）内から次画面へリダイレクトするための fire-and-forget ヘルパ。
	/// <para>
	/// hook の中で<b>同じレイヤー</b>の遷移を <c>await</c> すると恒久デッドロックする
	/// （新しい遷移は現在の遷移の完了を待ち、現在の遷移はその hook の完了を待つ相互待ち）。
	/// リダイレクトしたいときは await せず本拡張（＝<c>.Forget()</c> に意図のある名前を付けたもの）を使う。
	/// 発行された遷移は現在の遷移が完了した後に実行される。
	/// </para>
	/// <para>
	/// 既定の <see cref="InterruptPriority"/> は <see cref="InterruptPriority.Preempt"/>（実行中を即キャンセル）
	/// なので、他に待機中の遷移を巻き込みたくない場合はリダイレクト元の遷移を
	/// <see cref="InterruptPriority.Queue"/> で発行すること。例:
	/// <c>nav.Push(next, new PushOptions { InterruptPriority = InterruptPriority.Queue }).Redirect();</c>
	/// （hook から発行する現在の遷移自体は完走必須ゾーンにいるため、いずれの priority でも現遷移の後に走る。）
	/// </para>
	/// <para>
	/// なお「await を hook 内で禁止して fail-fast する再入ガード」は将来の検討事項。現状はこのヘルパと
	/// <see cref="IScreenNavigator"/> の注意書きで運用する。
	/// </para>
	/// </summary>
	public static class ScreenNavigatorRedirectExtensions
	{
		/// <summary>遷移タスクを await せず発行する（hook 内リダイレクト用）。現在の遷移完了後に実行される。</summary>
		public static void Redirect(this UniTask transition) => transition.Forget();

		/// <inheritdoc cref="Redirect(UniTask)"/>
		public static void Redirect<T>(this UniTask<T> transition) => transition.Forget();
	}
}
