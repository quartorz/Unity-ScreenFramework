using Cysharp.Threading.Tasks;
using ScreenFramework;

namespace Sample.Dialogs
{
	/// <summary>
	/// <see cref="MessageDialogView"/> 系の典型形を文字列だけで呼び出せる薄いラッパ。
	/// 「キャンセル / OK 二択を出して bool で受け取る」のような頻出パターンを Feature 側に書き散らさないため。
	/// </summary>
	public static class MessageDialogs
	{
		/// <summary>
		/// OK / Cancel 二択を出し、OK で <c>true</c> を返す。Cancel・閉じる(X)・外部破棄はいずれも <c>false</c>。
		/// </summary>
		public static async UniTask<bool> ConfirmAsync(
			string title,
			string message,
			string okText = "OK",
			string cancelText = "キャンセル")
		{
			var result = await ScreenNavigator.Dialog.PushAndAwait(new MessageDialogId(
				Title: title,
				Message: message,
				Buttons: new[] { cancelText, okText },
				ShowCloseButton: false));
			return result != null && result.Index == 1;
		}
	}
}
