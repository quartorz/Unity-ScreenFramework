namespace ScreenFramework
{
	/// <summary>
	/// ダイアログ系 Presenter の結果スロット。<see cref="Set"/> で結果を確定し、
	/// Hide / Unload 時に <see cref="WriteIfSet"/> で writer へ書き込む。
	/// <see cref="DialogPresenter{TInput,TOutput,TResult}"/> と
	/// <see cref="ComposedDialogPresenter{TInput,TOutput,TResult}"/> の双方から再利用する。
	///
	/// 結果未確定（Set 未呼び）のまま閉じられた場合は何も書かず、PushAndAwait は default(TResult) を返す。
	/// </summary>
	public sealed class DialogResultSlot<TResult> where TResult : INavigationData
	{
		TResult _result;
		bool _hasResult;

		/// <summary>結果を確定する。複数回呼べる（後勝ち）。</summary>
		public void Set(TResult value)
		{
			_result = value;
			_hasResult = true;
		}

		/// <summary>確定済みなら writer へ書き込む。未確定なら何もしない。</summary>
		public void WriteIfSet(INavigationDataWriter writer)
		{
			if (_hasResult) writer.Write(_result);
		}
	}
}
