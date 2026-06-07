using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sample.Api;
using Sample.Api.Net;
using Sample.Dialogs;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	public sealed class ProfilePresenter
		: SamplePresenter<MockView.Sample.IProfileViewInput, MockView.Sample.IProfileViewOutput>
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
			try
			{
				_current = await Registry.Profile.Get(_userId, new Options(ct));
				Apply(_current);
			}
			finally
			{
				Out.SetSaving(false);
			}
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
				_current = await Registry.Profile.Post(next, new Options(CancellationToken.None));
				Apply(_current);
			}
			catch (OperationCanceledException) { }
			catch (ApiException) { /* SystemDialog 表示済み、ユーザー確認済み */ }
			catch (ApiTransportException) { /* SystemDialog 表示済み、ユーザー確認済み */ }
			catch (Exception e)
			{
				Debug.LogError($"[ProfilePresenter] edit failed: {e}");
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
