using ScreenFramework;
using System;

namespace Sample.Dialogs
{
	public record SampleScreenId<TMockView, TView, TPresenter, TResult> : AddressableScreenId<TMockView, TResult>
		where TMockView : class, new()
		where TPresenter : IScreenPresenter
		where TResult : IScreenData
	{
		protected override string AddressableKey => $"Views/{typeof(TView).Name}";
		protected override IScreenPresenter MakePresenter() => Activator.CreateInstance<TPresenter>();
	}

	public sealed record InputDialogId(string Title, string InitialText)
		: SampleScreenId<MockView.Sample.Dialogs.MockInputDialogView, InputDialogView, InputDialogPresenter, InputDialogResult>
	{
		protected override IScreenPresenter MakePresenter() => new InputDialogPresenter(Title, InitialText);
	}
}
