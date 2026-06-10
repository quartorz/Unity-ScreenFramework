using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;

namespace Sample.Dialogs
{
	public sealed class InputDialogPresenter
		: SampleDialogPresenter<
			MockView.Sample.Dialogs.IInputDialogViewInput,
			MockView.Sample.Dialogs.IInputDialogViewOutput,
			InputDialogResult>
	{
		readonly string _title;
		readonly string _initial;

		public InputDialogPresenter(string title, string initial)
		{
			_title = title;
			_initial = initial;
		}

		protected override UniTask OnAfterLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			Out.SetTitle(_title);
			Out.SetInitialText(_initial);
			In.OnOkClicked += OnOk;
			In.OnCancelClicked += OnCancel;
			return UniTask.CompletedTask;
		}

		protected override UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct)
		{
			if (In != null)
			{
				In.OnOkClicked -= OnOk;
				In.OnCancelClicked -= OnCancel;
			}
			return UniTask.CompletedTask;
		}

		void OnOk()
		{
			SetResult(new InputDialogResult(Out.GetText()));
			ScreenNavigator.Close(this).Forget();
		}

		void OnCancel()
		{
			// SetResult を呼ばずに閉じる → PushAndAwait は default (null) を返す
			ScreenNavigator.Close(this).Forget();
		}
	}
}
