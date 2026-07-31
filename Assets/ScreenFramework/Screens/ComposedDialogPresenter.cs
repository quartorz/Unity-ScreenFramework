using System.Threading;
using Cysharp.Threading.Tasks;

namespace ScreenFramework
{
	/// <summary>
	/// 結果を 1 個返すダイアログを Part 合成で構成する Presenter 基底。
	/// <see cref="ComposedScreenPresenter{TInput,TOutput}"/> の合成機構に、
	/// <see cref="DialogPresenter{TInput,TOutput,TResult}"/> と同じ結果返却（<see cref="SetResult"/>）を載せたもの。
	///
	/// Hide / Unload 時の結果書き込みは基底が肩代わりしつつ Part への fan-out も維持する。
	/// 画面側の hide 反応ロジックは Presenter ではなく Part に持たせる方針（OnBeforeHideCore 等は設けない）。
	/// </summary>
	public abstract class ComposedDialogPresenter<TInput, TOutput, TResult> : ComposedScreenPresenter<TInput, TOutput>
		where TInput : class
		where TOutput : class
		where TResult : INavigationData
	{
		readonly DialogResultSlot<TResult> _result = new DialogResultSlot<TResult>();

		/// <summary>OK 等で結果を確定する。複数回呼べる（後勝ち）。</summary>
		protected void SetResult(TResult value) => _result.Set(value);

		protected sealed override async UniTask OnBeforeHide(INavigationDataWriter writer, ITransitionContext ctx, CancellationToken ct)
		{
			_result.WriteIfSet(writer);
			await base.OnBeforeHide(writer, ctx, ct);   // Part への fan-out（逆順）
		}

		// suspended のまま Resume を挟まず破棄される場合（KeepOnCover で覆われたダイアログを Close する等）は
		// Hide hook が走らないため、teardown 側の最後の書き込みチャンスで結果を落とさない。
		protected sealed override async UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct)
		{
			_result.WriteIfSet(writer);
			await base.OnAfterUnload(writer, ct);       // Part への fan-out（逆順）
		}
	}
}
