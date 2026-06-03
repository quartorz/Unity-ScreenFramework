using ScreenFramework;

namespace Sample.Dialogs
{
	public sealed record InputDialogId(string Title, string InitialText)
		: SampleScreenId<MockView.Sample.Dialogs.MockInputDialogView, InputDialogView, InputDialogPresenter, InputDialogResult>
	{
		protected override IScreenPresenter MakePresenter() => new InputDialogPresenter(Title, InitialText);
	}
}
