using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sample.Api;
using Sample.Api.Net;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	public sealed class TitlePresenter
		: SamplePresenter<MockView.Sample.ITitleViewInput, MockView.Sample.ITitleViewOutput>
	{
		bool _ready;

		protected override UniTask OnAfterLoad(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			Debug.Log("[TitlePresenter] OnAfterLoad");
			Out.SetTitle("MockView Sample");
			Out.SetStartButtonInteractable(false);
			Out.SetStatus("起動データを取得中...");
			In.OnStartClicked += OnStart;
			return UniTask.CompletedTask;
		}

		protected override async UniTask OnAfterEnter(INavigationDataReader reader, ITransitionContext ctx, CancellationToken ct)
		{
			try
			{
				var opt = new Options(ct);
				var (master, userInfo) = await UniTask.WhenAll(
					Registry.Api.Master.Bootstrap(opt),
					Registry.Api.User.Info(opt));

				Registry.Items.SetData(master.items.Select(r => new ItemMaster
				{
					Id = r.id,
					Code = r.code,
					Name = r.name,
					Rarity = r.rarity,
				}));
				Registry.UserData.SetInfo(new UserInfo
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
			catch (ApiException)
			{
				// SystemDialog で表示済み。Title はリトライ手段がないので status だけ更新。
				Out.SetStatus("取得失敗");
			}
			catch (ApiTransportException)
			{
				Out.SetStatus("取得失敗");
			}
			catch (Exception e)
			{
				Out.SetStatus($"取得失敗: {e.Message}");
				Debug.LogError($"[TitlePresenter] bootstrap fetch failed: {e}");
			}
		}

		protected override UniTask OnAfterUnload(INavigationDataWriter writer, CancellationToken ct)
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
