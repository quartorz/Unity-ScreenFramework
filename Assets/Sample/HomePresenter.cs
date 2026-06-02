using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	public sealed class HomePresenter
		: SamplePresenter<MockView.Sample.IHomeViewInput, MockView.Sample.IHomeViewOutput>
	{
		protected override UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			Out.SetTitle("Home Screen");
			In.OnGoProfileClicked += OnGoProfile;
			Debug.Log("[HomePresenter] OnAfterLoad");
			return UniTask.CompletedTask;
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (In != null)
			{
				In.OnGoProfileClicked -= OnGoProfile;
			}
			Debug.Log("[HomePresenter] OnAfterUnload");
			return UniTask.CompletedTask;
		}

		void OnGoProfile()
		{
			ScreenNavigator.Page.Push(new ProfileScreenId(Services.UserData.Info.UserId)).Forget();
		}
	}
}
