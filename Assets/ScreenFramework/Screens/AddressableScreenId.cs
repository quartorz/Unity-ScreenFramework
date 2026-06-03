namespace ScreenFramework
{
	/// <summary>
	/// Addressables + Mock 切替の定型を肩代わりする ScreenIdentifier 基底。
	/// 派生では <see cref="AddressableKey"/> と <see cref="MakePresenter"/> だけ書けばよい。
	/// </summary>
	/// <typeparam name="TMockView">UseMockViews=true 時に new で生成するモック View 型</typeparam>
	public abstract record AddressableScreenId<TMockView> : ScreenIdentifier
		where TMockView : class, new()
	{
		protected abstract string AddressableKey { get; }
		protected abstract IScreenPresenter MakePresenter();

		public sealed override IScreenHandle CreateHandle(ScreenServices services)
			=> services.UseMockViews
				? new MockScreenHandle<TMockView>()
				: new AddressableScreenHandle(AddressableKey);

		public sealed override IScreenPresenter CreatePresenter(ScreenServices services)
			=> MakePresenter();
	}

	/// <summary>
	/// 結果を返すダイアログ向け。<see cref="ScreenIdentifier{TResult}"/> 派生版。
	/// </summary>
	public abstract record AddressableScreenId<TMockView, TResult> : ScreenIdentifier<TResult>
		where TMockView : class, new()
		where TResult : IScreenData
	{
		protected abstract string AddressableKey { get; }
		protected abstract IScreenPresenter MakePresenter();

		public sealed override IScreenHandle CreateHandle(ScreenServices services)
			=> services.UseMockViews
				? new MockScreenHandle<TMockView>()
				: new AddressableScreenHandle(AddressableKey);

		public sealed override IScreenPresenter CreatePresenter(ScreenServices services)
			=> MakePresenter();
	}
}
