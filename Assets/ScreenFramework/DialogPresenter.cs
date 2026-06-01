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
		where TResult : IScreenData
	{
		TResult _result;
		bool _hasResult;

		/// <summary>OK 等で結果を確定する。複数回呼べる（後勝ち）。</summary>
		protected void SetResult(TResult value)
		{
			_result = value;
			_hasResult = true;
		}

		/// <summary>OnBeforeExit を固定化して writer 書き込みを基底に閉じ込める。</summary>
		protected sealed override UniTask OnBeforeExit(IScreenDataWriter writer, CancellationToken ct)
		{
			if (_hasResult) writer.Write(_result);
			return OnBeforeExitCore(ct);
		}

		/// <summary>派生側はこちらを override する。</summary>
		protected virtual UniTask OnBeforeExitCore(CancellationToken ct) => UniTask.CompletedTask;
	}
}
