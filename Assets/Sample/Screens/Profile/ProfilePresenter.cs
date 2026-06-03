using System.Threading;
using Cysharp.Threading.Tasks;
using Sample.Api;
using Sample.Dialogs;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	public sealed class ProfilePresenter
		: SamplePresenter<MockView.Sample.IProfileViewInput, MockView.Sample.IProfileViewOutput, ProfileView>
	{
		readonly string _userId;
		ProfileResponse _current;
		bool _busy;

		public ProfilePresenter(string userId) { _userId = userId; }

		protected override async UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			Debug.Log($"[ProfilePresenter] OnAfterLoad UserId={_userId}");
			In.OnEditNameClicked += OnEditName;
			In.OnBackClicked += OnBack;

			Out.SetSaving(true);
			_current = await Services.Api.GetProfile(_userId, ct);
			Apply(_current);
			Out.SetSaving(false);
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (In != null)
			{
				In.OnEditNameClicked -= OnEditName;
				In.OnBackClicked -= OnBack;
			}
			return UniTask.CompletedTask;
		}

		void Apply(ProfileResponse p)
		{
			Out.SetUserId(p.userId);
			Out.SetLevel(p.level);
			Out.SetName(p.name);
		}

		void OnEditName()
		{
			if (_busy) return;
			EditNameAsync().Forget();
		}

		async UniTaskVoid EditNameAsync()
		{
			_busy = true;
			try
			{
				var result = await ScreenNavigator.Dialog.PushAndAwait(
					new InputDialogId("名前を編集", _current.name));
				if (result == null || string.IsNullOrEmpty(result.Text)) return;

				Out.SetSaving(true);
				var next = new ProfileRequest
				{
					userId = _current.userId,
					name = result.Text,
					level = _current.level,
				};
				_current = await Services.Api.PostProfile(next, CancellationToken.None);
				Apply(_current);
			}
			catch (System.OperationCanceledException) { /* 黙って戻る */ }
			catch (System.Exception e)
			{
				Debug.LogError($"[ProfilePresenter] edit failed: {e.Message}");
			}
			finally
			{
				Out.SetSaving(false);
				_busy = false;
			}
		}

		void OnBack()
		{
			ScreenNavigator.Page.Pop().Forget();
		}
	}
}
