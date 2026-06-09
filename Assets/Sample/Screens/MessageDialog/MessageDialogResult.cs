using ScreenFramework;

namespace Sample.Dialogs
{
	/// <summary>
	/// MessageDialog で押されたボタンの index（0 始まり）。
	/// 右上の閉じる(X)ボタンで閉じた / 外部から破棄された場合は SetResult されないので
	/// <see cref="ScreenFramework.IScreenNavigator.PushAndAwait{TResult}"/> は default(null) を返す。
	/// </summary>
	public sealed record MessageDialogResult(int Index) : INavigationData;
}
