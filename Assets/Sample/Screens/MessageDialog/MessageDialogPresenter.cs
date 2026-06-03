using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;

namespace Sample.Dialogs
{
	public sealed class MessageDialogPresenter
		: SampleDialogPresenter<
			MockView.Sample.Dialogs.IMessageDialogViewInput,
			MockView.Sample.Dialogs.IMessageDialogViewOutput,
			MessageDialogView,
			MessageDialogResult>
	{
		readonly string _title;
		readonly string _message;
		readonly string[] _buttons;
		readonly bool _showCloseButton;

		public MessageDialogPresenter(string title, string message, string[] buttons, bool showCloseButton)
		{
			_title = title;
			_message = message;
			_buttons = buttons ?? new[] { "OK" };
			_showCloseButton = showCloseButton;
		}

		protected override UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			Out.SetTitle(_title);
			Out.SetMessage(_message);
			Out.SetButtons(_buttons);
			Out.SetCloseButtonVisible(_showCloseButton);
			In.OnButtonClicked += OnButton;
			In.OnCloseClicked += OnClose;
			return UniTask.CompletedTask;
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (In != null)
			{
				In.OnButtonClicked -= OnButton;
				In.OnCloseClicked -= OnClose;
			}
			return UniTask.CompletedTask;
		}

		void OnButton(int index)
		{
			SetResult(new MessageDialogResult(index));
			ScreenNavigator.Close(this).Forget();
		}

		void OnClose()
		{
			// SetResult を呼ばずに閉じる → PushAndAwait は default(null) を返す
			ScreenNavigator.Close(this).Forget();
		}
	}
}
