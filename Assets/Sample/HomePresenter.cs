using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	public sealed class HomePresenter : ScreenPresenter<MockView.Sample.IHomeView>
	{
		protected override UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			View.SetTitle("Home Screen");
			View.OnGoDetailClicked += OnGoDetail;
			Debug.Log("[HomePresenter] OnAfterLoad");
			return UniTask.CompletedTask;
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (View != null) View.OnGoDetailClicked -= OnGoDetail;
			Debug.Log("[HomePresenter] OnAfterUnload");
			return UniTask.CompletedTask;
		}

		void OnGoDetail()
		{
			ScreenNavigator.Page.Push(new DetailScreenId("user-001")).Forget();
		}
	}

	public sealed class DetailPresenter : ScreenPresenter<MockView.Sample.IDetailView>
	{
		readonly string _userId;
		public DetailPresenter(string userId) { _userId = userId; }

		protected override UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			View.SetUserId(_userId);
			View.OnBackClicked += OnBack;
			Debug.Log($"[DetailPresenter] OnAfterLoad UserId={_userId}");
			return UniTask.CompletedTask;
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (View != null) View.OnBackClicked -= OnBack;
			Debug.Log("[DetailPresenter] OnAfterUnload");
			return UniTask.CompletedTask;
		}

		void OnBack()
		{
			ScreenNavigator.Page.Pop().Forget();
		}
	}
}
