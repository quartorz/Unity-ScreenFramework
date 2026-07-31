using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// <see cref="IScreenNavigator.Push"/> や <see cref="IScreenNavigator.FindEntry{T}"/> が返す
	/// 「特定の画面エントリへのハンドル」抽象。
	/// チュートリアル等で特定の画面の Presenter を保持し、後から操作したり閉じたりするのに使う。
	/// </summary>
	public interface IScreenEntry
	{
		/// <summary>Presenter インスタンス。具体型へキャストして使う（<see cref="ScreenEntryExtensions.As{T}"/> 推奨）。</summary>
		IScreenPresenter Presenter { get; }

		/// <summary>まだ Navigator のスタックに存在するか。Pop / Cover+Destroy で消されたら false。</summary>
		bool IsAlive { get; }

		/// <summary>このエントリを閉じる。死んでいれば no-op。</summary>
		UniTask Close(PopOptions opt = default, CancellationToken ct = default);
	}

	public static class ScreenEntryExtensions
	{
		/// <summary>
		/// Presenter を型 <typeparamref name="T"/> として取り出す。
		/// entry が null、Presenter が null、型違いなら null を返す。
		/// </summary>
		public static T As<T>(this IScreenEntry entry) where T : class, IScreenPresenter
			=> entry?.Presenter as T;

		/// <summary>
		/// IsAlive が false になるまで待つ。既に false なら即完了。
		/// Pop / Cover+Destroy など、閉じられる経路を問わず必ず完了する。
		/// </summary>
		public static UniTask WaitClosedAsync(this IScreenEntry entry, CancellationToken ct = default)
			=> entry == null ? UniTask.CompletedTask : UniTask.WaitWhile(() => entry.IsAlive, cancellationToken: ct);
	}
}
