using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	public sealed class TitlePresenter
		: SamplePresenter<MockView.Sample.ITitleViewInput, MockView.Sample.ITitleViewOutput, TitleView>
	{
		bool _ready;

		protected override async UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			Debug.Log("[TitlePresenter] OnAfterLoad");
			Out.SetTitle("MockView Sample");
			Out.SetStartButtonInteractable(false);
			Out.SetStatus("起動データを取得中...");
			In.OnStartClicked += OnStart;

			try
			{
				var (master, userInfo) = await UniTask.WhenAll(
					Services.Api.GetBootstrapMaster(ct),
					Services.Api.GetUserInfo(ct));

				Services.Items.SetData(master.items.Select(r => new ItemMaster
				{
					Id = r.id,
					Code = r.code,
					Name = r.name,
					Rarity = r.rarity,
				}));
				Services.UserData.SetInfo(new UserInfo
				{
					UserId = userInfo.userId,
					Name = userInfo.name,
					Level = userInfo.level,
					Money = userInfo.money,
				});

				_ready = true;
				Out.SetStatus($"アイテムマスタ {master.items.Length} 件 / ユーザー {userInfo.name}");
				Out.SetStartButtonInteractable(true);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				Out.SetStatus($"取得失敗: {e.Message}");
				Debug.LogError($"[TitlePresenter] bootstrap master fetch failed: {e}");
			}
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (In != null) In.OnStartClicked -= OnStart;
			return UniTask.CompletedTask;
		}

		void OnStart()
		{
			if (!_ready) return;
			ScreenNavigator.Page.Change(new HomeScreenId()).Forget();
		}
	}
}
