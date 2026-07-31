using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// 結果を 1 個返す画面（主にダイアログ）用の Presenter 基底。
	/// 画面側は <see cref="SetResult"/> で結果を確定するだけでよく、
	/// writer への書き込みは基底が肩代わりする（型ミス防止）。
	///
	/// 対応する Identifier は <see cref="ScreenIdentifier{TResult}"/>。
	/// 結果未確定（SetResult 未呼び）のまま閉じられた場合、PushAndAwait は default(TResult) を返す。
	/// </summary>
	public abstract class DialogPresenter<TInput, TOutput, TResult> : ScreenPresenter<TInput, TOutput>
		where TInput : class
		where TOutput : class
		where TResult : INavigationData
	{
		TResult _result;
		bool _hasResult;

		/// <summary>OK 等で結果を確定する。複数回呼べる（後勝ち）。</summary>
		protected void SetResult(TResult value)
		{
			_result = value;
			_hasResult = true;
		}

		/// <summary>OnBeforeHide を固定化して writer 書き込みを基底に閉じ込める。</summary>
		protected sealed override UniTask OnBeforeHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct)
		{
			if (_hasResult) writer.Write(_result);
			return OnBeforeHideCore(ctx, ct);
		}

		/// <summary>派生側はこちらを override する。</summary>
		protected virtual UniTask OnBeforeHideCore(ITransitionContext ctx, CancellationToken ct) => UniTask.CompletedTask;

		/// <summary>
		/// OnAfterUnload も固定化して結果を書く。suspended のまま Resume を挟まず破棄される場合
		/// （KeepOnCover で覆われたダイアログを Close する等）は Exit hook が走らないため、
		/// teardown 側の最後の書き込みチャンスで結果を落とさない（Exit 経由で書き込み済みなら同値の上書きで無害）。
		/// </summary>
		protected sealed override UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct)
		{
			if (_hasResult) writer.Write(_result);
			return OnAfterUnloadCore(writer, ct);
		}

		/// <summary>派生側はこちらを override する。</summary>
		protected virtual UniTask OnAfterUnloadCore(INavigationDataWriter writer, CancellationToken ct) => UniTask.CompletedTask;
	}
}
