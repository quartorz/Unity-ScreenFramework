namespace ScreenFramework
{
	public interface IScreenIdentifier
	{
		IScreenHandle CreateHandle(ScreenServices services);
		IScreenPresenter CreatePresenter(ScreenServices services);
		ScreenCacheMode? CachePolicy { get; }
	}

	public abstract record ScreenIdentifier : IScreenIdentifier
	{
		public abstract IScreenHandle CreateHandle(ScreenServices services);
		public abstract IScreenPresenter CreatePresenter(ScreenServices services);
		public virtual ScreenCacheMode? CachePolicy => null;
	}

	/// <summary>
	/// 結果を返すダイアログ用 Identifier。TResult が PushAndAwait の戻り型と
	/// DialogPresenter の SetResult 引数型を縛る。
	/// </summary>
	public abstract record ScreenIdentifier<TResult> : ScreenIdentifier
		where TResult : IScreenData
	{ }
}
