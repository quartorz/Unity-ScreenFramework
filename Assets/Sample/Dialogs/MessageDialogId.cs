namespace Sample.Dialogs
{
	/// <summary>
	/// 汎用メッセージダイアログ。
	/// <paramref name="Buttons"/> の各ラベルがボタンとして表示され、押された index を <see cref="MessageDialogResult"/> で返す。
	/// <paramref name="ShowCloseButton"/> が true なら右上に X ボタンを表示し、そこで閉じると null（キャンセル相当）を返す。
	/// </summary>
	public sealed record MessageDialogId(string Title, string Message, string[] Buttons, bool ShowCloseButton = false)
		: SampleScreenId<
			MockView.Sample.Dialogs.MockMessageDialogView,
			MessageDialogView,
			MessageDialogPresenter,
			MessageDialogResult>
	{
		protected override ScreenFramework.IScreenPresenter MakePresenter()
			=> new MessageDialogPresenter(Title, Message, Buttons, ShowCloseButton);
	}
}
