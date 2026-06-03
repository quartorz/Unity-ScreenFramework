using ScreenFramework;
using System;

namespace Sample
{
	public record SampleScreenId<TMockView, TView, TPresenter> : AddressableScreenId<TMockView>
		where TMockView : class, new()
		where TPresenter : IScreenPresenter
	{
		protected override string AddressableKey => $"Views/{typeof(TView).Name}";
		protected override IScreenPresenter MakePresenter() => Activator.CreateInstance<TPresenter>();
	}

	public record SampleScreenId<TMockView, TView, TPresenter, TResult> : AddressableScreenId<TMockView, TResult>
		where TMockView : class, new()
		where TPresenter : IScreenPresenter
		where TResult : IScreenData
	{
		protected override string AddressableKey => $"Views/{typeof(TView).Name}";
		protected override IScreenPresenter MakePresenter() => Activator.CreateInstance<TPresenter>();
	}
}
