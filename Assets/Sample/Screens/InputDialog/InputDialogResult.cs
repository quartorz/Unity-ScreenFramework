using ScreenFramework;

namespace Sample.Dialogs
{
	public sealed record InputDialogResult(string Text) : INavigationData;
}
