using System.Threading;
using Cysharp.Threading.Tasks;
using ScreenFramework;
using UnityEngine;

namespace Sample
{
	public sealed class HomePresenter
		: SamplePresenter<MockView.Sample.IHomeViewInput, MockView.Sample.IHomeViewOutput, HomeView>
	{
		protected override UniTask OnAfterLoad(IScreenDataReader reader, CancellationToken ct)
		{
			Out.SetTitle("Home Screen");
			In.goProfile.OnClicked += OnGoProfile;
			In.goGacha.OnClicked += OnGoGacha;
			Debug.Log("[HomePresenter] OnAfterLoad");
			return UniTask.CompletedTask;
		}

		protected override UniTask OnAfterUnload(IScreenDataWriter writer, CancellationToken ct)
		{
			if (In != null)
			{
				In.goProfile.OnClicked -= OnGoProfile;
				In.goGacha.OnClicked -= OnGoGacha;
			}
			Debug.Log("[HomePresenter] OnAfterUnload");
			return UniTask.CompletedTask;
		}

		void OnGoProfile()
		{
			ScreenNavigator.Page.Push(new ProfileScreenId(Registry.UserData.Info.UserId)).Forget();
		}

		void OnGoGacha()
		{
			ScreenNavigator.Page.Push(new GachaTopScreenId()).Forget();
		}
	}
}
